package outbox_test

import (
	"context"
	"errors"
	"testing"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/events"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events/outbox"
	outboxmemory "github.com/MathiasHilgert/frappe-api/internal/foundation/events/outbox/memory"
)

type orderCreated struct {
	OrderID string `json:"orderId"`
}

var created = events.Define[orderCreated]("outboxtest.created", 1)

func TestRecorderAppendsEnvelopeInUnitOfWork(t *testing.T) {
	store := outboxmemory.NewStore()
	var recorder events.Recorder = outbox.NewRecorder(store)
	event := created.With(orderCreated{OrderID: "order-1"}).ForEntity("order-1", 1)

	err := store.Within(context.Background(), func(ctx context.Context) error {
		return recorder.Record(ctx, event)
	})
	if err != nil {
		t.Fatalf("Record: %v", err)
	}

	claimed, _ := store.Claim(context.Background(), 10, time.Minute)
	if len(claimed) != 1 {
		t.Fatalf("stored %d messages, want 1", len(claimed))
	}
	message := claimed[0].Message
	if message.ID != event.ID || message.Subject != created.Type() {
		t.Fatalf("stored message %+v", message)
	}
	decoded, err := created.Decode(message.Payload)
	if err != nil || decoded.Data.OrderID != "order-1" || decoded.Sequence != 1 {
		t.Fatalf("stored envelope decodes to %+v, %v", decoded, err)
	}
}

func TestRecorderPropagatesStoreErrors(t *testing.T) {
	recorder := outbox.NewRecorder(outboxmemory.NewStore())
	err := recorder.Record(context.Background(), created.With(orderCreated{}))
	if !errors.Is(err, outboxmemory.ErrNoUnitOfWork) {
		t.Fatalf("Record outside a unit of work = %v, want ErrNoUnitOfWork", err)
	}
}

func TestRecorderRejectsInvalidEvents(t *testing.T) {
	store := outboxmemory.NewStore()
	recorder := outbox.NewRecorder(store)
	err := store.Within(context.Background(), func(ctx context.Context) error {
		return recorder.Record(ctx, created.With(orderCreated{}).ForEntity("", 2))
	})
	if !errors.Is(err, events.ErrInvalidEnvelope) {
		t.Fatalf("Record = %v, want ErrInvalidEnvelope", err)
	}
}
