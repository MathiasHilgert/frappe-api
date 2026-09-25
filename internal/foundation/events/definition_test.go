package events_test

import (
	"testing"
	"time"

	"github.com/google/uuid"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/events"
)

type orderCreated struct {
	OrderID string `json:"orderId"`
	Total   int64  `json:"total"`
}

var definitionCreated = events.Define[orderCreated]("definitiontest.created", 1)

func TestDefineBuildsTypeAndSource(t *testing.T) {
	if got, want := definitionCreated.Type(), "frappe.definitiontest.created.v1"; got != want {
		t.Fatalf("Type() = %q, want %q", got, want)
	}
	if got, want := definitionCreated.Source(), "frappe-api/definitiontest"; got != want {
		t.Fatalf("Source() = %q, want %q", got, want)
	}
}

func TestDefinePanicsOnInvalidInput(t *testing.T) {
	cases := map[string]struct {
		name    string
		version int
	}{
		"empty name":          {name: "", version: 1},
		"single segment":      {name: "orders", version: 1},
		"uppercase":           {name: "Orders.created", version: 1},
		"empty segment":       {name: "orders..created", version: 1},
		"leading digit":       {name: "orders.1created", version: 1},
		"zero version":        {name: "invalidtest.created", version: 0},
		"negative version":    {name: "invalidtest.created", version: -1},
		"non ascii character": {name: "orders.creäted", version: 1},
	}
	for name, testCase := range cases {
		t.Run(name, func(t *testing.T) {
			assertPanics(t, func() { events.Define[orderCreated](testCase.name, testCase.version) })
		})
	}
}

func TestDefinePanicsOnDuplicateType(t *testing.T) {
	events.Define[orderCreated]("duplicatetest.created", 1)
	assertPanics(t, func() { events.Define[orderCreated]("duplicatetest.created", 1) })
	// A new version of the same event is a distinct type.
	events.Define[orderCreated]("duplicatetest.created", 2)
}

func TestWithBuildsEvent(t *testing.T) {
	before := time.Now().UTC()
	event := definitionCreated.With(orderCreated{OrderID: "order-1", Total: 42})

	parsed, err := uuid.Parse(event.ID)
	if err != nil {
		t.Fatalf("ID %q is not a UUID: %v", event.ID, err)
	}
	if parsed.Version() != 7 {
		t.Fatalf("ID version = %d, want 7", parsed.Version())
	}
	if event.Type != "frappe.definitiontest.created.v1" || event.Source != "frappe-api/definitiontest" {
		t.Fatalf("unexpected type/source: %q %q", event.Type, event.Source)
	}
	if event.Time.Location() != time.UTC || event.Time.Before(before) {
		t.Fatalf("Time = %v, want UTC not before %v", event.Time, before)
	}
	if event.Data.OrderID != "order-1" || event.EntityKey != "" || event.Sequence != 0 {
		t.Fatalf("unexpected event: %+v", event)
	}
	if other := definitionCreated.With(orderCreated{}); other.ID == event.ID {
		t.Fatal("two events share the same ID")
	}
}

func TestForEntityReturnsCopy(t *testing.T) {
	event := definitionCreated.With(orderCreated{OrderID: "order-1"})
	keyed := event.ForEntity("order-1", 3)

	if keyed.EntityKey != "order-1" || keyed.Sequence != 3 {
		t.Fatalf("ForEntity = %+v", keyed)
	}
	if event.EntityKey != "" || event.Sequence != 0 {
		t.Fatal("ForEntity mutated the original event")
	}
	if keyed.ID != event.ID {
		t.Fatal("ForEntity changed the ID")
	}
}

func assertPanics(t *testing.T, function func()) {
	t.Helper()
	defer func() {
		if recover() == nil {
			t.Fatal("expected a panic")
		}
	}()
	function()
}
