package inbox_test

import (
	"context"
	"errors"
	"os"
	"sync/atomic"
	"testing"
	"time"

	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
	sdkmetric "go.opentelemetry.io/otel/sdk/metric"
	"go.opentelemetry.io/otel/sdk/metric/metricdata"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/events"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events/inbox"
	inboxmemory "github.com/MathiasHilgert/frappe-api/internal/foundation/events/inbox/memory"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events/memory"
)

var metricReader = sdkmetric.NewManualReader()

func TestMain(m *testing.M) {
	otel.SetMeterProvider(sdkmetric.NewMeterProvider(sdkmetric.WithReader(metricReader)))
	os.Exit(m.Run())
}

type paid struct {
	Order string `json:"order"`
}

var paymentReceived = events.Define[paid]("inboxtest.paid", 1)

const eventually = 5 * time.Second

func settings() inbox.Settings {
	return inbox.Settings{PurgeInterval: time.Hour, Retention: time.Hour}
}

func TestConsumersHandleEachEventOnceAndCountDuplicates(t *testing.T) {
	registry := events.NewRegistry()
	var handled atomic.Int32
	events.On(registry.Module("billing"), paymentReceived, func(context.Context, events.Event[paid]) error {
		handled.Add(1)
		return nil
	})
	broker := memory.NewBroker()
	consumers := inbox.NewConsumers(registry, broker, inboxmemory.NewStore(), settings())
	if err := consumers.Start(t.Context()); err != nil {
		t.Fatalf("Start: %v", err)
	}
	defer func() { _ = consumers.Stop(context.Background()) }()

	message, err := events.NewMessage(context.Background(), paymentReceived.With(paid{Order: "o-1"}))
	if err != nil {
		t.Fatal(err)
	}
	// The counter is cumulative across -count runs; assert the increase.
	baseline := duplicates(t, "billing-inboxtest-paid-v1")
	for range 3 {
		if err := broker.Publish(context.Background(), message); err != nil {
			t.Fatal(err)
		}
	}
	waitFor(t, func() bool { return duplicates(t, "billing-inboxtest-paid-v1") == baseline+2 })
	if got := handled.Load(); got != 1 {
		t.Fatalf("handled %d times, want 1", got)
	}
}

func TestConsumersStartFailsWhenASubscriptionFails(t *testing.T) {
	registry := events.NewRegistry()
	events.On(registry.Module("billing"), paymentReceived, func(context.Context, events.Event[paid]) error { return nil })
	failure := errors.New("broker down")
	consumers := inbox.NewConsumers(registry, failingSubscriber{err: failure}, inboxmemory.NewStore(), settings())
	if err := consumers.Start(t.Context()); !errors.Is(err, failure) {
		t.Fatalf("Start = %v, want %v", err, failure)
	}
}

func TestConsumersPurgeOldRecords(t *testing.T) {
	store := &countingStore{Store: inboxmemory.NewStore()}
	consumers := inbox.NewConsumers(events.NewRegistry(), memory.NewBroker(), store,
		inbox.Settings{PurgeInterval: 10 * time.Millisecond, Retention: time.Hour})
	if err := consumers.Start(t.Context()); err != nil {
		t.Fatal(err)
	}
	waitFor(t, func() bool { return store.purges.Load() >= 2 })
	if err := consumers.Stop(context.Background()); err != nil {
		t.Fatalf("Stop: %v", err)
	}
}

func TestConsumersStopRejectsDeliveriesAfterStop(t *testing.T) {
	registry := events.NewRegistry()
	var handled atomic.Int32
	events.On(registry.Module("billing"), paymentReceived, func(context.Context, events.Event[paid]) error {
		handled.Add(1)
		return nil
	})
	subscriber := &capturingSubscriber{}
	consumers := inbox.NewConsumers(registry, subscriber, inboxmemory.NewStore(), settings())
	if err := consumers.Start(t.Context()); err != nil {
		t.Fatal(err)
	}
	if err := consumers.Stop(context.Background()); err != nil {
		t.Fatal(err)
	}
	if err := subscriber.handler(context.Background(), events.Message{ID: "late"}); !errors.Is(err, inbox.ErrStopped) {
		t.Fatalf("late delivery = %v, want ErrStopped", err)
	}
	if handled.Load() != 0 {
		t.Fatal("a delivery after Stop was handled")
	}
}

type failingSubscriber struct{ err error }

func (subscriber failingSubscriber) Subscribe(context.Context, events.Subscription, events.Handler) error {
	return subscriber.err
}

type capturingSubscriber struct{ handler events.Handler }

func (subscriber *capturingSubscriber) Subscribe(_ context.Context, _ events.Subscription, handler events.Handler) error {
	subscriber.handler = handler
	return nil
}

type countingStore struct {
	inbox.Store
	purges atomic.Int32
}

func (store *countingStore) Purge(ctx context.Context, before time.Time) (int, error) {
	store.purges.Add(1)
	return store.Store.Purge(ctx, before)
}

func waitFor(t *testing.T, condition func() bool) {
	t.Helper()
	deadline := time.Now().Add(eventually)
	for !condition() {
		if time.Now().After(deadline) {
			t.Fatal("timed out")
		}
		time.Sleep(5 * time.Millisecond)
	}
}

// duplicates returns the frappe.events.duplicates sum for consumer.
func duplicates(t *testing.T, consumer string) int64 {
	t.Helper()
	var collected metricdata.ResourceMetrics
	if err := metricReader.Collect(context.Background(), &collected); err != nil {
		t.Fatal(err)
	}
	for _, scope := range collected.ScopeMetrics {
		for _, recorded := range scope.Metrics {
			if recorded.Name != "frappe.events.duplicates" {
				continue
			}
			sum, _ := recorded.Data.(metricdata.Sum[int64])
			for _, point := range sum.DataPoints {
				if value, _ := point.Attributes.Value(attribute.Key("consumer")); value.AsString() == consumer {
					return point.Value
				}
			}
		}
	}
	return 0
}
