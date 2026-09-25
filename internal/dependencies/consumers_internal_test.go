package dependencies

import (
	"context"
	"sync/atomic"
	"testing"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/application"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events/inbox"
	inboxmemory "github.com/MathiasHilgert/frappe-api/internal/foundation/events/inbox/memory"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events/memory"
)

// fixturePayload and fixtureCreated are the published language of a test
// module fixture; no real module exists yet.
type fixturePayload struct {
	Name string `json:"name"`
}

var fixtureCreated = events.Define[fixturePayload]("fixture.created", 1)

// fixtureSubscriptions is what a module's Subscriptions(registry) would do.
func fixtureSubscriptions(registry *events.Registry, handled *atomic.Int32) {
	events.On(registry.Module("fixture"), fixtureCreated, func(context.Context, events.Event[fixturePayload]) error {
		handled.Add(1)
		return nil
	})
}

func inboxConfiguration() configuration.Inbox {
	return configuration.Inbox{PurgeInterval: time.Hour, Retention: time.Hour}
}

func memoryInbox() (inbox.Store, error) { return inboxmemory.NewStore(), nil }

func TestProvideConsumersRegistersNothingWithoutABroker(t *testing.T) {
	if provideConsumers(application.New(), inboxConfiguration(), eventBroker{}, events.NewRegistry(), memoryInbox) {
		t.Fatal("consumers registered without a broker")
	}
}

func TestProvideConsumersRunsRegisteredSubscriptionsThroughTheInbox(t *testing.T) {
	instance := application.New()
	broker := memory.NewBroker()
	registry := events.NewRegistry()
	var handled atomic.Int32
	fixtureSubscriptions(registry, &handled)

	if !provideConsumers(instance, inboxConfiguration(), eventBroker{Publisher: broker, Subscriber: broker}, registry, memoryInbox) {
		t.Fatal("consumers not registered with a broker")
	}
	if err := instance.Up(t.Context()); err != nil {
		t.Fatalf("Up: %v", err)
	}
	defer func() { _ = instance.Down(context.Background()) }()

	message, err := events.NewMessage(context.Background(), fixtureCreated.With(fixturePayload{Name: "a"}))
	if err != nil {
		t.Fatal(err)
	}
	for range 2 {
		if err := broker.Publish(context.Background(), message); err != nil {
			t.Fatal(err)
		}
	}
	deadline := time.Now().Add(5 * time.Second)
	for handled.Load() == 0 && time.Now().Before(deadline) {
		time.Sleep(5 * time.Millisecond)
	}
	time.Sleep(50 * time.Millisecond)
	if got := handled.Load(); got != 1 {
		t.Fatalf("handled %d times, want 1", got)
	}
}

func TestResolveOutboxPublisherDefaultsToTheBroker(t *testing.T) {
	broker := memory.NewBroker()
	if got := resolveOutboxPublisher(nil, eventBroker{Publisher: broker}); got != events.Publisher(broker) {
		t.Fatal("the broker publisher was not used")
	}
	override := memory.NewBroker()
	if got := resolveOutboxPublisher(override, eventBroker{Publisher: broker}); got != events.Publisher(override) {
		t.Fatal("WithPublisher did not override the broker publisher")
	}
}
