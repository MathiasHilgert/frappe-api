package outbox_test

import (
	"context"
	"errors"
	"os"
	"sync"
	"sync/atomic"
	"testing"
	"time"

	"go.opentelemetry.io/otel"
	sdkmetric "go.opentelemetry.io/otel/sdk/metric"
	"go.opentelemetry.io/otel/sdk/metric/metricdata"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/events"
	brokermemory "github.com/MathiasHilgert/frappe-api/internal/foundation/events/memory"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events/outbox"
	outboxmemory "github.com/MathiasHilgert/frappe-api/internal/foundation/events/outbox/memory"
)

var metricReader = sdkmetric.NewManualReader()

func TestMain(m *testing.M) {
	otel.SetMeterProvider(sdkmetric.NewMeterProvider(sdkmetric.WithReader(metricReader)))
	os.Exit(m.Run())
}

// flakyPublisher fails the first failures publishes, then delegates.
type flakyPublisher struct {
	next      events.Publisher
	attempts  []time.Time
	failures  int
	mutex     sync.Mutex
	published atomic.Int64
}

func (publisher *flakyPublisher) Publish(ctx context.Context, message events.Message) error {
	publisher.mutex.Lock()
	publisher.attempts = append(publisher.attempts, time.Now())
	failing := len(publisher.attempts) <= publisher.failures
	publisher.mutex.Unlock()
	if failing {
		return errors.New("broker unavailable")
	}
	publisher.published.Add(1)
	if publisher.next == nil {
		return nil
	}
	return publisher.next.Publish(ctx, message)
}

func (publisher *flakyPublisher) attemptTimes() []time.Time {
	publisher.mutex.Lock()
	defer publisher.mutex.Unlock()
	return append([]time.Time(nil), publisher.attempts...)
}

// storeWithoutNotifications hides the memory store's notifications to
// exercise the polling path.
type storeWithoutNotifications struct {
	*outboxmemory.Store
}

func (storeWithoutNotifications) Notifications() <-chan struct{} {
	return nil
}

func record(t *testing.T, store *outboxmemory.Store, count int) {
	t.Helper()
	recorder := outbox.NewRecorder(store)
	err := store.Within(context.Background(), func(ctx context.Context) error {
		for index := range count {
			if err := recorder.Record(ctx, created.With(orderCreated{OrderID: string(rune('a' + index))})); err != nil {
				return err
			}
		}
		return nil
	})
	if err != nil {
		t.Fatal(err)
	}
}

func waitFor(t *testing.T, description string, condition func() bool) {
	t.Helper()
	deadline := time.Now().Add(5 * time.Second)
	for !condition() {
		if time.Now().After(deadline) {
			t.Fatalf("timed out waiting for %s", description)
		}
		time.Sleep(5 * time.Millisecond)
	}
}

func pendingCount(t *testing.T, store *outboxmemory.Store) bool {
	t.Helper()
	_, found, err := store.OldestPending(context.Background())
	if err != nil {
		t.Fatal(err)
	}
	return found
}

func TestRunOncePublishesEveryPendingMessage(t *testing.T) {
	store := outboxmemory.NewStore()
	broker := brokermemory.NewBroker()
	received := make(chan events.Message, 10)
	err := broker.Subscribe(t.Context(), events.Subscription{Name: "relay", Subject: created.Type()},
		func(_ context.Context, message events.Message) error {
			received <- message
			return nil
		})
	if err != nil {
		t.Fatal(err)
	}
	record(t, store, 3)

	relay := outbox.NewRelay(store, broker, outbox.Settings{BatchSize: 2})
	published, err := relay.RunOnce(context.Background())
	if err != nil || published != 3 {
		t.Fatalf("RunOnce = %d, %v; want 3 across two batches", published, err)
	}
	if pendingCount(t, store) {
		t.Fatal("published messages are still pending")
	}
	for range 3 {
		select {
		case message := <-received:
			if _, err := created.Decode(message.Payload); err != nil {
				t.Fatalf("relayed payload does not decode: %v", err)
			}
		case <-time.After(5 * time.Second):
			t.Fatal("relayed message not delivered")
		}
	}
}

func TestRunOnceBacksOffFailedMessages(t *testing.T) {
	store := outboxmemory.NewStore()
	publisher := &flakyPublisher{failures: 2}
	record(t, store, 1)
	relay := outbox.NewRelay(store, publisher, outbox.Settings{BaseBackoff: 100 * time.Millisecond, MaxBackoff: time.Second})

	if published, err := relay.RunOnce(context.Background()); err != nil || published != 0 {
		t.Fatalf("first RunOnce = %d, %v", published, err)
	}
	if published, _ := relay.RunOnce(context.Background()); published != 0 || len(publisher.attemptTimes()) != 1 {
		t.Fatal("a failed message must not be retried before its backoff")
	}

	waitFor(t, "publication after retries", func() bool {
		_, _ = relay.RunOnce(context.Background())
		return publisher.published.Load() == 1
	})
	attempts := publisher.attemptTimes()
	if len(attempts) != 3 {
		t.Fatalf("publish attempts = %d, want 3", len(attempts))
	}
	// Exponential backoff: 100ms after the first failure, 200ms after the second.
	if gap := attempts[1].Sub(attempts[0]); gap < 100*time.Millisecond {
		t.Fatalf("first retry after %v, want >= 100ms", gap)
	}
	if gap := attempts[2].Sub(attempts[1]); gap < 200*time.Millisecond {
		t.Fatalf("second retry after %v, want >= 200ms", gap)
	}
	if pendingCount(t, store) {
		t.Fatal("message still pending after a successful retry")
	}
}

func TestBackoffIsCapped(t *testing.T) {
	cases := map[int]time.Duration{0: time.Second, 1: 2 * time.Second, 2: 4 * time.Second, 3: 5 * time.Second, 60: 5 * time.Second}
	settings := outbox.Settings{BaseBackoff: time.Second, MaxBackoff: 5 * time.Second}
	for attempts, want := range cases {
		if got := settings.Backoff(attempts); got != want {
			t.Errorf("Backoff(%d) = %v, want %v", attempts, got, want)
		}
	}
}

func TestStartWakesOnNotifications(t *testing.T) {
	store := outboxmemory.NewStore()
	publisher := &flakyPublisher{}
	relay := outbox.NewRelay(store, publisher, outbox.Settings{PollInterval: time.Hour})
	if err := relay.Start(context.Background()); err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { _ = relay.Stop(context.Background()) })

	record(t, store, 1)
	waitFor(t, "notified publication", func() bool { return publisher.published.Load() == 1 })
}

func TestStartPollsWithoutNotifications(t *testing.T) {
	store := outboxmemory.NewStore()
	publisher := &flakyPublisher{}
	relay := outbox.NewRelay(storeWithoutNotifications{store}, publisher, outbox.Settings{PollInterval: 20 * time.Millisecond})
	if err := relay.Start(context.Background()); err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { _ = relay.Stop(context.Background()) })

	record(t, store, 2)
	waitFor(t, "polled publication", func() bool { return publisher.published.Load() == 2 })
}

func TestStartAndStopLifecycle(t *testing.T) {
	relay := outbox.NewRelay(outboxmemory.NewStore(), &flakyPublisher{}, outbox.Settings{})
	if err := relay.Stop(context.Background()); err != nil {
		t.Fatalf("Stop before Start = %v", err)
	}
	if err := relay.Start(context.Background()); err != nil {
		t.Fatal(err)
	}
	if err := relay.Start(context.Background()); err == nil {
		t.Fatal("second Start must fail")
	}
	if err := relay.Stop(context.Background()); err != nil {
		t.Fatalf("Stop = %v", err)
	}
	if err := relay.Start(context.Background()); err != nil {
		t.Fatalf("restart after Stop = %v", err)
	}
	if err := relay.Stop(context.Background()); err != nil {
		t.Fatalf("Stop = %v", err)
	}
}

func TestStartPurgesPublishedMessages(t *testing.T) {
	store := outboxmemory.NewStore()
	record(t, store, 2)
	counting := &purgeCounting{Store: store}
	relay := outbox.NewRelay(counting, &flakyPublisher{}, outbox.Settings{PollInterval: 10 * time.Millisecond, PurgeInterval: 10 * time.Millisecond, Retention: time.Nanosecond})
	if err := relay.Start(context.Background()); err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { _ = relay.Stop(context.Background()) })

	waitFor(t, "purge by the relay", func() bool { return counting.purged.Load() == 2 })
}

// purgeCounting counts the messages purged through it.
type purgeCounting struct {
	*outboxmemory.Store
	purged atomic.Int64
}

func (store *purgeCounting) Purge(ctx context.Context, publishedBefore time.Time) (int, error) {
	purged, err := store.Store.Purge(ctx, publishedBefore)
	store.purged.Add(int64(purged))
	return purged, err
}

func TestRelayMetrics(t *testing.T) {
	store := outboxmemory.NewStore()
	record(t, store, 2)
	relay := outbox.NewRelay(store, &flakyPublisher{failures: 1}, outbox.Settings{})
	if _, err := relay.RunOnce(context.Background()); err != nil {
		t.Fatal(err)
	}

	metrics := map[string]metricdata.Aggregation{}
	var data metricdata.ResourceMetrics
	if err := metricReader.Collect(context.Background(), &data); err != nil {
		t.Fatal(err)
	}
	for _, scope := range data.ScopeMetrics {
		for _, found := range scope.Metrics {
			metrics[found.Name] = found.Data
		}
	}
	if sum := counterTotal(metrics["frappe.outbox.published"]); sum < 1 {
		t.Fatalf("frappe.outbox.published = %d, want >= 1", sum)
	}
	if sum := counterTotal(metrics["frappe.outbox.failed"]); sum < 1 {
		t.Fatalf("frappe.outbox.failed = %d, want >= 1", sum)
	}
	gauge, ok := metrics["frappe.outbox.lag"].(metricdata.Gauge[float64])
	if !ok || len(gauge.DataPoints) == 0 || gauge.DataPoints[0].Value <= 0 {
		t.Fatalf("frappe.outbox.lag = %+v, want a positive age while a message is pending", metrics["frappe.outbox.lag"])
	}
}

func counterTotal(aggregation metricdata.Aggregation) int64 {
	sum, ok := aggregation.(metricdata.Sum[int64])
	if !ok {
		return 0
	}
	var total int64
	for _, point := range sum.DataPoints {
		total += point.Value
	}
	return total
}
