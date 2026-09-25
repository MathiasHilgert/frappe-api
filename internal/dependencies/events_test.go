package dependencies

import (
	"context"
	"testing"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/application"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events/memory"
)

func TestProvideEventsWithoutABrokerRegistersNothing(t *testing.T) {
	for _, broker := range []string{"", configuration.EventsBrokerNone} {
		instance := application.New()
		broker := provideEvents(instance, configuration.Configuration{Events: configuration.Events{Broker: broker}})
		if broker.Publisher != nil || broker.Subscriber != nil {
			t.Fatalf("broker = %+v, want no publisher or subscriber", broker)
		}
		if checks := instance.Checks(); len(checks) != 0 {
			t.Fatalf("registered checks %v, want none", checks)
		}
	}
}

func TestProvideEventsSelectsTheMemoryBroker(t *testing.T) {
	broker := provideEvents(application.New(), configuration.Configuration{Events: configuration.Events{Broker: configuration.EventsBrokerMemory}})
	if _, ok := broker.Publisher.(*memory.Broker); !ok {
		t.Fatalf("publisher = %T, want *memory.Broker", broker.Publisher)
	}
	if subscriber, _ := broker.Subscriber.(*memory.Broker); subscriber != broker.Publisher {
		t.Fatal("memory publisher and subscriber must be the same broker")
	}
}

func TestProvideEventsRegistersNATSWithACheck(t *testing.T) {
	instance := application.New()
	broker := provideEvents(instance, configuration.Configuration{
		Events: configuration.Events{Broker: configuration.EventsBrokerNATS},
		NATS:   configuration.NATS{URL: "nats://127.0.0.1:1"},
	})
	if len(instance.Checks()) != 1 {
		t.Fatalf("registered %d checks, want the nats check", len(instance.Checks()))
	}
	// Before Up the adapters fail instead of panicking.
	if err := broker.Publisher.Publish(context.Background(), events.Message{ID: "1", Subject: "frappe.test.v1"}); err == nil {
		t.Fatal("Publish succeeded before the NATS client was up")
	}
	subscription := events.Subscription{Name: "test", Subject: "frappe.test.v1"}
	if err := broker.Subscriber.Subscribe(context.Background(), subscription, func(context.Context, events.Message) error { return nil }); err == nil {
		t.Fatal("Subscribe succeeded before the NATS client was up")
	}
}
