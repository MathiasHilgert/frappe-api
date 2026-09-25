package memory_test

import (
	"context"
	"errors"
	"testing"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/events"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events/brokertest"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events/memory"
)

func TestBrokerContract(t *testing.T) {
	brokertest.Run(t, func(*testing.T) (events.Publisher, events.Subscriber) {
		broker := memory.NewBroker()
		return broker, broker
	})
}

func TestPublishHonorsContext(t *testing.T) {
	ctx, cancel := context.WithCancel(context.Background())
	cancel()
	err := memory.NewBroker().Publish(ctx, events.Message{ID: "1", Subject: "frappe.memory.cancelled.v1"})
	if !errors.Is(err, context.Canceled) {
		t.Fatalf("Publish() = %v, want context.Canceled", err)
	}
}

func TestPublishCopiesTheMessage(t *testing.T) {
	broker := memory.NewBroker()
	delivered := make(chan events.Message, 1)
	subscription := events.Subscription{Name: "copy", Subject: "frappe.memory.copy.v1"}
	if err := broker.Subscribe(t.Context(), subscription, func(_ context.Context, message events.Message) error {
		delivered <- message
		return nil
	}); err != nil {
		t.Fatal(err)
	}

	message := events.Message{ID: "1", Subject: subscription.Subject, Payload: []byte("original"), Headers: map[string]string{"key": "original"}}
	if err := broker.Publish(context.Background(), message); err != nil {
		t.Fatal(err)
	}
	message.Payload[0] = 'X'
	message.Headers["key"] = "changed"

	got := <-delivered
	if string(got.Payload) != "original" || got.Headers["key"] != "original" {
		t.Fatalf("delivered message shares memory with the published one: %+v", got)
	}
}
