//go:build integration

package river_test

import (
	"context"
	"errors"
	"slices"
	"sync"
	"testing"
	"time"

	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgxpool"
	"go.opentelemetry.io/otel/attribute"
	sdkmetric "go.opentelemetry.io/otel/sdk/metric"
	"go.opentelemetry.io/otel/sdk/metric/metricdata"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/database"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/database/databasetest"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/jobs"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/jobs/queuetest"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/jobs/river"
)

// eventually bounds every asynchronous expectation in this file.
const eventually = 30 * time.Second

// replicaReaders holds, per test, one metric reader per started client, so
// every client behaves like a separate replica exporting its own series.
var (
	replicaReaders      = map[*testing.T][]*sdkmetric.ManualReader{}
	replicaReadersMutex sync.Mutex
)

// testSettings polls fast so tests stay quick, and records metrics on a
// reader of its own registered for t.
func testSettings(t *testing.T) river.Settings {
	t.Helper()
	reader := sdkmetric.NewManualReader()
	replicaReadersMutex.Lock()
	if _, found := replicaReaders[t]; !found {
		t.Cleanup(func() {
			replicaReadersMutex.Lock()
			defer replicaReadersMutex.Unlock()
			delete(replicaReaders, t)
		})
	}
	replicaReaders[t] = append(replicaReaders[t], reader)
	replicaReadersMutex.Unlock()
	return river.Settings{
		Workers:           5,
		FetchPollInterval: 100 * time.Millisecond,
		MetricsInterval:   200 * time.Millisecond,
		MeterProvider:     sdkmetric.NewMeterProvider(sdkmetric.WithReader(reader)),
	}
}

// startClient builds and starts a client on pool for catalog, stopped when
// the test ends.
func startClient(t *testing.T, pool *pgxpool.Pool, catalog *jobs.Catalog) *river.Client {
	t.Helper()
	client, err := river.New(pool, catalog, testSettings(t))
	if err != nil {
		t.Fatalf("New() = %v", err)
	}
	if err := client.Start(context.Background()); err != nil {
		t.Fatalf("Start() = %v", err)
	}
	t.Cleanup(func() {
		ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		if err := client.Stop(ctx); err != nil {
			t.Errorf("Stop() = %v", err)
		}
	})
	return client
}

func TestIntegrationContract(t *testing.T) {
	queuetest.Run(t, func(t *testing.T, catalog *jobs.Catalog) jobs.Enqueuer {
		return startClient(t, databasetest.New(t), catalog)
	})
}

type noArguments struct{}

func countJobs(t *testing.T, pool *pgxpool.Pool, name string) int {
	t.Helper()
	var count int
	if err := pool.QueryRow(context.Background(), "SELECT count(*) FROM river_job WHERE kind = $1", name).Scan(&count); err != nil {
		t.Fatal(err)
	}
	return count
}

func TestIntegrationEnqueueJoinsTheContextTransaction(t *testing.T) {
	pool := databasetest.New(t)
	catalog := jobs.NewCatalog()
	module := catalog.Module("orders")
	definition := jobs.Define[noArguments](module, "send_receipt")
	jobs.Handle(module, definition, func(context.Context, jobs.Job[noArguments]) error { return nil })
	client, err := river.New(pool, catalog, testSettings(t))
	if err != nil {
		t.Fatal(err)
	}
	catalog.Use(client)
	ctx := context.Background()

	rollback := errors.New("business rule failed")
	err = database.WithinTransaction(ctx, pool, nil, func(ctx context.Context, _ pgx.Tx) error {
		if _, enqueueErr := definition.Enqueue(ctx, noArguments{}); enqueueErr != nil {
			return enqueueErr
		}
		return rollback
	})
	if !errors.Is(err, rollback) {
		t.Fatalf("WithinTransaction() = %v", err)
	}
	if count := countJobs(t, pool, definition.Name()); count != 0 {
		t.Fatalf("rolled back transaction left %d jobs, want 0", count)
	}

	err = database.WithinTransaction(ctx, pool, nil, func(ctx context.Context, _ pgx.Tx) error {
		_, enqueueErr := definition.Enqueue(ctx, noArguments{})
		return enqueueErr
	})
	if err != nil {
		t.Fatal(err)
	}
	if count := countJobs(t, pool, definition.Name()); count != 1 {
		t.Fatalf("committed transaction left %d jobs, want 1", count)
	}
}

func TestIntegrationPeriodicJobRunsOncePerTickAcrossClients(t *testing.T) {
	pool := databasetest.New(t)
	catalog := jobs.NewCatalog()
	module := catalog.Module("reports")
	definition := jobs.Define[noArguments](module, "compile_daily")
	var mutex sync.Mutex
	var executions []time.Time
	jobs.Handle(module, definition, func(_ context.Context, job jobs.Job[noArguments]) error {
		mutex.Lock()
		defer mutex.Unlock()
		executions = append(executions, job.EnqueuedAt)
		return nil
	})
	jobs.Every(definition, time.Second, noArguments{})

	startClient(t, pool, catalog)
	startClient(t, pool, catalog)

	const wanted = 4
	deadline := time.Now().Add(eventually)
	for {
		mutex.Lock()
		count := len(executions)
		mutex.Unlock()
		if count >= wanted {
			break
		}
		if time.Now().After(deadline) {
			t.Fatalf("got %d periodic executions, want %d", count, wanted)
		}
		time.Sleep(100 * time.Millisecond)
	}

	mutex.Lock()
	enqueued := slices.Clone(executions)
	mutex.Unlock()
	slices.SortFunc(enqueued, func(left, right time.Time) int { return left.Compare(right) })
	for index := 1; index < len(enqueued); index++ {
		if gap := enqueued[index].Sub(enqueued[index-1]); gap < 500*time.Millisecond {
			t.Fatalf("two jobs enqueued %s apart: a tick ran on both clients (%v)", gap, enqueued)
		}
	}
	if ticks := sumCounter(t, "frappe.jobs.periodic.ticks"); ticks < wanted {
		t.Fatalf("frappe.jobs.periodic.ticks = %d, want at least %d", ticks, wanted)
	}
	waitForGauge(t, "frappe.jobs.leader", nil, func(total int64) bool { return total == 1 })
	assertNoAttributes(t, "frappe.jobs.leader")
}

func TestIntegrationReportsQueueDepthAndHealth(t *testing.T) {
	pool := databasetest.New(t)
	catalog := jobs.NewCatalog()
	module := catalog.Module("depth")
	definition := jobs.Define[noArguments](module, "later", jobs.WithQueue("depth_queue"))
	jobs.Handle(module, definition, func(context.Context, jobs.Job[noArguments]) error { return nil })
	client := startClient(t, pool, catalog)
	// A second replica on the same database: only the leader reports
	// queue depth, so the gauge is not counted twice.
	startClient(t, pool, catalog)
	catalog.Use(client)
	ctx := context.Background()
	if _, err := definition.Enqueue(ctx, noArguments{}, jobs.After(time.Hour)); err != nil {
		t.Fatal(err)
	}
	if _, err := definition.Enqueue(ctx, noArguments{}, jobs.Queue("unknown_queue")); !errors.Is(err, river.ErrUnknownQueue) {
		t.Fatalf("Enqueue() on an unconfigured queue = %v, want ErrUnknownQueue", err)
	}
	filter := []attribute.KeyValue{attribute.String("queue", "depth_queue"), attribute.String("state", "scheduled")}
	waitForGauge(t, "frappe.jobs.leader", nil, func(total int64) bool { return total == 1 })
	waitForGauge(t, "frappe.jobs.queue.depth", filter, func(total int64) bool { return total == 1 })
	time.Sleep(time.Second)
	if total := gaugeTotal(t, "frappe.jobs.leader", nil); total != 1 {
		t.Fatalf("frappe.jobs.leader = %d across two replicas, want 1", total)
	}
	if total := gaugeTotal(t, "frappe.jobs.queue.depth", filter); total != 1 {
		t.Fatalf("frappe.jobs.queue.depth = %d across two replicas, want 1 (leader only)", total)
	}
	if err := client.Check(context.Background()); err != nil {
		t.Fatalf("Check() = %v", err)
	}
}

func TestIntegrationNewRejectsUnhandledJobs(t *testing.T) {
	catalog := jobs.NewCatalog()
	jobs.Define[noArguments](catalog.Module("orders"), "orphan")
	if _, err := river.New(databasetest.New(t), catalog, testSettings(t)); err == nil {
		t.Fatal("New() must reject a catalog with an unhandled job")
	}
}

// collect returns the scope metrics of every replica of t.
func collect(t *testing.T) []metricdata.ScopeMetrics {
	t.Helper()
	replicaReadersMutex.Lock()
	readers := slices.Clone(replicaReaders[t])
	replicaReadersMutex.Unlock()
	scopes := make([]metricdata.ScopeMetrics, 0, len(readers))
	for _, reader := range readers {
		var collected metricdata.ResourceMetrics
		if err := reader.Collect(context.Background(), &collected); err != nil {
			t.Fatal(err)
		}
		scopes = append(scopes, collected.ScopeMetrics...)
	}
	return scopes
}

func sumCounter(t *testing.T, name string) int64 {
	t.Helper()
	var total int64
	for _, scope := range collect(t) {
		for _, instrument := range scope.Metrics {
			if sum, ok := instrument.Data.(metricdata.Sum[int64]); ok && instrument.Name == name {
				for _, point := range sum.DataPoints {
					total += point.Value
				}
			}
		}
	}
	return total
}

func gaugeTotal(t *testing.T, name string, filter []attribute.KeyValue) int64 {
	t.Helper()
	var total int64
	for _, scope := range collect(t) {
		for _, instrument := range scope.Metrics {
			gauge, ok := instrument.Data.(metricdata.Gauge[int64])
			if !ok || instrument.Name != name {
				continue
			}
		points:
			for _, point := range gauge.DataPoints {
				for _, wanted := range filter {
					if value, found := point.Attributes.Value(wanted.Key); !found || value != wanted.Value {
						continue points
					}
				}
				total += point.Value
			}
		}
	}
	return total
}

func waitForGauge(t *testing.T, name string, filter []attribute.KeyValue, satisfied func(int64) bool) {
	t.Helper()
	deadline := time.Now().Add(eventually)
	for {
		total := gaugeTotal(t, name, filter)
		if satisfied(total) {
			return
		}
		if time.Now().After(deadline) {
			t.Fatalf("%s = %d never satisfied the expectation", name, total)
		}
		time.Sleep(200 * time.Millisecond)
	}
}

func TestIntegrationKeepsWorkingAfterTheStartContextIsCancelled(t *testing.T) {
	pool := databasetest.New(t)
	catalog := jobs.NewCatalog()
	module := catalog.Module("orders")
	definition := jobs.Define[noArguments](module, "after_start")
	handled := make(chan struct{}, 1)
	jobs.Handle(module, definition, func(context.Context, jobs.Job[noArguments]) error {
		handled <- struct{}{}
		return nil
	})
	client, err := river.New(pool, catalog, testSettings(t))
	if err != nil {
		t.Fatal(err)
	}
	// The application lifecycle cancels the Up phase context as soon as
	// Up returns; the client must outlive it.
	startCtx, cancel := context.WithCancel(context.Background())
	if err := client.Start(startCtx); err != nil {
		t.Fatal(err)
	}
	cancel()
	t.Cleanup(func() { _ = client.Stop(context.Background()) })
	catalog.Use(client)

	if _, err := definition.Enqueue(context.Background(), noArguments{}); err != nil {
		t.Fatal(err)
	}
	select {
	case <-handled:
	case <-time.After(eventually):
		t.Fatal("no job ran after the start context was cancelled")
	}
}

func assertNoAttributes(t *testing.T, name string) {
	t.Helper()
	for _, scope := range collect(t) {
		for _, instrument := range scope.Metrics {
			gauge, ok := instrument.Data.(metricdata.Gauge[int64])
			if !ok || instrument.Name != name {
				continue
			}
			for _, point := range gauge.DataPoints {
				if point.Attributes.Len() != 0 {
					t.Fatalf("%s carries attributes %v; per-process attributes explode cardinality", name, point.Attributes.ToSlice())
				}
			}
		}
	}
}
