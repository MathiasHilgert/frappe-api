package eventstest_test

import (
	"context"
	"errors"
	"sync"
	"testing"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/events"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events/eventstest"
)

type orderCreated struct {
	OrderID string
}

type orderCancelled struct {
	Reason string
}

var (
	created   = events.Define[orderCreated]("eventstesttest.created", 1)
	cancelled = events.Define[orderCancelled]("eventstesttest.cancelled", 1)
)

func TestRecordedReturnsTypedEventsInOrder(t *testing.T) {
	recorder := eventstest.NewRecorder()
	ctx := context.Background()

	_ = recorder.Record(ctx, created.With(orderCreated{OrderID: "a"}))
	_ = recorder.Record(ctx, cancelled.With(orderCancelled{Reason: "late"}))
	_ = recorder.Record(ctx, created.With(orderCreated{OrderID: "b"}))

	got := eventstest.Recorded(t, recorder, created)
	if len(got) != 2 || got[0].Data.OrderID != "a" || got[1].Data.OrderID != "b" {
		t.Fatalf("Recorded(created) = %+v", got)
	}
	if others := eventstest.Recorded(t, recorder, cancelled); len(others) != 1 || others[0].Data.Reason != "late" {
		t.Fatalf("Recorded(cancelled) = %+v", others)
	}
	if all := recorder.All(); len(all) != 3 {
		t.Fatalf("All() has %d events, want 3", len(all))
	}
}

func TestRecordedOnlyReturnsTheSameVersion(t *testing.T) {
	recorder := eventstest.NewRecorder()
	createdV2 := events.Define[orderCreated]("eventstesttest.created", 2)
	_ = recorder.Record(context.Background(), createdV2.With(orderCreated{}))

	if got := eventstest.Recorded(t, recorder, created); len(got) != 0 {
		t.Fatalf("v1 lookup returned %d v2 events", len(got))
	}
}

func TestFailMakesRecordReturnTheError(t *testing.T) {
	recorder := eventstest.NewRecorder()
	failure := errors.New("outbox unavailable")
	recorder.Fail(failure)

	if err := recorder.Record(context.Background(), created.With(orderCreated{})); !errors.Is(err, failure) {
		t.Fatalf("Record() error = %v, want %v", err, failure)
	}
	if len(recorder.All()) != 0 {
		t.Fatal("a failed Record must not capture the event")
	}

	recorder.Fail(nil)
	if err := recorder.Record(context.Background(), created.With(orderCreated{})); err != nil {
		t.Fatalf("Record() after Fail(nil) = %v", err)
	}
}

func TestResetForgetsEvents(t *testing.T) {
	recorder := eventstest.NewRecorder()
	_ = recorder.Record(context.Background(), created.With(orderCreated{}))
	recorder.Reset()
	if len(recorder.All()) != 0 {
		t.Fatal("Reset must forget recorded events")
	}
}

func TestRecordRejectsUnencodableEvents(t *testing.T) {
	recorder := eventstest.NewRecorder()
	invalid := created.With(orderCreated{}).ForEntity("", 1)
	if err := recorder.Record(context.Background(), invalid); err == nil {
		t.Fatal("expected the same validation as the production recorder")
	}
}

func TestRecorderIsConcurrencySafe(t *testing.T) {
	recorder := eventstest.NewRecorder()
	var group sync.WaitGroup
	for range 50 {
		group.Go(func() {
			_ = recorder.Record(context.Background(), created.With(orderCreated{}))
			_ = recorder.All()
		})
	}
	group.Wait()
	if got := len(eventstest.Recorded(t, recorder, created)); got != 50 {
		t.Fatalf("recorded %d events, want 50", got)
	}
}
