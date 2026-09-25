//go:build integration

package dependencies_test

import (
	"context"
	"errors"
	"sync"
	"sync/atomic"
	"testing"
	"time"

	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgxpool"
	testcontainersnats "github.com/testcontainers/testcontainers-go/modules/nats"
	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
	sdkmetric "go.opentelemetry.io/otel/sdk/metric"
	"go.opentelemetry.io/otel/sdk/metric/metricdata"
	sdktrace "go.opentelemetry.io/otel/sdk/trace"
	"go.opentelemetry.io/otel/sdk/trace/tracetest"
	"go.opentelemetry.io/otel/trace"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/database"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/database/databasetest"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events/inbox"
	inboxpostgres "github.com/MathiasHilgert/frappe-api/internal/foundation/events/inbox/postgres"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events/outbox"
	outboxpostgres "github.com/MathiasHilgert/frappe-api/internal/foundation/events/outbox/postgres"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/nats"
)

// flowImage matches the NATS image pinned in compose.yaml.
const flowImage = "nats:2.15.0-alpine"

// flowDuplicateWindow is kept short so the test can republish the same
// event ID after JetStream stopped deduplicating it, the case only the
// inbox protects against.
const flowDuplicateWindow = time.Second

const flowEventually = 20 * time.Second

type orderPlaced struct {
	Order string `json:"order"`
}

var orderPlacedEvent = events.Define[orderPlaced]("flowtest.placed", 1)

// flow is the running event platform of one test.
type flow struct {
	ownerPool       *pgxpool.Pool
	applicationPool *pgxpool.Pool
	relayPool       *pgxpool.Pool
	recorder        *outbox.Recorder
	spans           *tracetest.SpanRecorder
	metrics         *sdkmetric.ManualReader
	consumerTraces  sync.Map
	handled         atomic.Int32
}

// TestIntegrationEventFlowIsAtomicTracedAndExactlyOnce proves the whole
// flow on real Postgres and NATS: a use case records an event together
// with a business row, the relay publishes it, a registered consumer
// handles it through the inbox exactly once even when the relay publishes
// it again, a rolled back use case records nothing, and the consumer span
// continues the producer's trace.
func TestIntegrationEventFlowIsAtomicTracedAndExactlyOnce(t *testing.T) {
	current := startFlow(t)
	ctx := t.Context()

	producerTrace := current.placeOrder(t, "order-1", nil)
	waitUntil(t, "the event is handled", func() bool { return current.handled.Load() == 1 })
	if got := current.count(t, current.ownerPool, `SELECT count(*) FROM receipts`); got != 1 {
		t.Fatalf("receipts = %d, want 1", got)
	}

	// Trace continuity: the consumer span belongs to the producer's trace.
	consumerTrace, _ := current.consumerTraces.Load("order-1")
	if consumerTrace != producerTrace {
		t.Fatalf("consumer trace %v, want producer trace %v", consumerTrace, producerTrace)
	}

	// A rolled back use case stores neither the business row nor the event.
	failure := errors.New("business rule violated")
	current.placeOrder(t, "order-2", failure)
	if got := current.count(t, current.ownerPool, `SELECT count(*) FROM orders`); got != 1 {
		t.Fatalf("orders = %d after a rollback, want 1", got)
	}
	if got := current.count(t, current.relayPool, `SELECT count(*) FROM outbox`); got != 1 {
		t.Fatalf("outbox rows = %d after a rollback, want 1", got)
	}

	// The relay republishes the same event after the broker's duplicate
	// window, as it would after losing a publish acknowledgement: the broker
	// delivers it again and the inbox skips it.
	baseline := current.duplicates(t)
	time.Sleep(flowDuplicateWindow + 500*time.Millisecond)
	if _, err := current.relayPool.Exec(ctx, `UPDATE outbox SET published_at = NULL`); err != nil {
		t.Fatal(err)
	}
	waitUntil(t, "the republished event is skipped as a duplicate", func() bool { return current.duplicates(t) == baseline+1 })
	if got := current.handled.Load(); got != 1 {
		t.Fatalf("handled %d times, want exactly 1", got)
	}
	if got := current.count(t, current.ownerPool, `SELECT count(*) FROM receipts`); got != 1 {
		t.Fatalf("receipts = %d after a duplicate, want 1", got)
	}
}

func startFlow(t *testing.T) *flow {
	t.Helper()
	current := &flow{spans: tracetest.NewSpanRecorder(), metrics: sdkmetric.NewManualReader()}
	otel.SetTracerProvider(sdktrace.NewTracerProvider(sdktrace.WithSpanProcessor(current.spans)))
	otel.SetMeterProvider(sdkmetric.NewMeterProvider(sdkmetric.WithReader(current.metrics)))

	current.ownerPool, current.applicationPool, current.relayPool = databasetest.NewWithEveryRole(t)
	if _, err := current.ownerPool.Exec(t.Context(),
		`CREATE TABLE orders (id text PRIMARY KEY); CREATE TABLE receipts (order_id text PRIMARY KEY)`); err != nil {
		t.Fatal(err)
	}
	client := startBroker(t)

	registry := events.NewRegistry()
	events.On(registry.Module("flowtest"), orderPlacedEvent, current.handle)
	consumers := inbox.NewConsumers(registry, client, inboxpostgres.NewStore(current.applicationPool),
		inbox.Settings{PurgeInterval: time.Hour, Retention: time.Hour})
	if err := consumers.Start(t.Context()); err != nil {
		t.Fatalf("start consumers: %v", err)
	}
	t.Cleanup(func() { _ = consumers.Stop(context.Background()) })

	relayStore := outboxpostgres.NewStore(current.relayPool)
	if err := relayStore.Start(t.Context()); err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { _ = relayStore.Stop(context.Background()) })
	relay := outbox.NewRelay(relayStore, client, outbox.Settings{PollInterval: 100 * time.Millisecond, Lease: 5 * time.Second})
	if err := relay.Start(t.Context()); err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { _ = relay.Stop(context.Background()) })

	current.recorder = outbox.NewRecorder(outboxpostgres.NewStore(nil))
	return current
}

func startBroker(t *testing.T) *nats.Client {
	t.Helper()
	container, err := testcontainersnats.Run(t.Context(), flowImage)
	if err != nil {
		t.Fatalf("start nats container: %v", err)
	}
	t.Cleanup(func() { _ = container.Terminate(context.Background()) })
	url, err := container.ConnectionString(t.Context())
	if err != nil {
		t.Fatal(err)
	}
	client, err := nats.Up(t.Context(), nats.Settings{URL: url, DuplicateWindow: flowDuplicateWindow})
	if err != nil {
		t.Fatalf("nats Up: %v", err)
	}
	t.Cleanup(func() { _ = nats.Down(context.Background(), client) })
	return client
}

// placeOrder is a use case: it inserts an order and records its event in
// one transaction, failing with failure (when not nil) after both. It
// returns the producer's trace ID.
func (current *flow) placeOrder(t *testing.T, order string, failure error) trace.TraceID {
	t.Helper()
	ctx, span := otel.Tracer("flowtest").Start(t.Context(), "place order")
	defer span.End()
	err := database.WithinTransaction(ctx, current.applicationPool, nil, func(ctx context.Context, transaction pgx.Tx) error {
		if _, err := transaction.Exec(ctx, `INSERT INTO orders (id) VALUES ($1)`, order); err != nil {
			return err
		}
		if err := current.recorder.Record(ctx, orderPlacedEvent.With(orderPlaced{Order: order})); err != nil {
			return err
		}
		return failure
	})
	if !errors.Is(err, failure) {
		t.Fatalf("place order = %v, want %v", err, failure)
	}
	return span.SpanContext().TraceID()
}

// handle is the consumer: it writes a receipt in the inbox transaction.
func (current *flow) handle(ctx context.Context, event events.Event[orderPlaced]) error {
	transaction, ok := database.TransactionFromContext(ctx)
	if !ok {
		return errors.New("handler ctx carries no inbox transaction")
	}
	if _, err := transaction.Exec(ctx, `INSERT INTO receipts (order_id) VALUES ($1)`, event.Data.Order); err != nil {
		return err
	}
	current.consumerTraces.Store(event.Data.Order, trace.SpanContextFromContext(ctx).TraceID())
	current.handled.Add(1)
	return nil
}

func (current *flow) count(t *testing.T, pool *pgxpool.Pool, statement string) int {
	t.Helper()
	var count int
	if err := pool.QueryRow(t.Context(), statement).Scan(&count); err != nil {
		t.Fatal(err)
	}
	return count
}

// duplicates returns the frappe.events.duplicates sum of the consumer.
func (current *flow) duplicates(t *testing.T) int64 {
	t.Helper()
	var collected metricdata.ResourceMetrics
	if err := current.metrics.Collect(context.Background(), &collected); err != nil {
		t.Fatal(err)
	}
	for _, scope := range collected.ScopeMetrics {
		for _, recorded := range scope.Metrics {
			sum, ok := recorded.Data.(metricdata.Sum[int64])
			if recorded.Name != "frappe.events.duplicates" || !ok {
				continue
			}
			for _, point := range sum.DataPoints {
				if value, _ := point.Attributes.Value(attribute.Key("consumer")); value.AsString() == "flowtest-flowtest-placed-v1" {
					return point.Value
				}
			}
		}
	}
	return 0
}

func waitUntil(t *testing.T, description string, condition func() bool) {
	t.Helper()
	deadline := time.Now().Add(flowEventually)
	for !condition() {
		if time.Now().After(deadline) {
			t.Fatalf("timed out waiting until %s", description)
		}
		time.Sleep(20 * time.Millisecond)
	}
}
