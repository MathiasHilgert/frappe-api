package events_test

import (
	"errors"
	"fmt"
	"testing"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/events"
)

func TestSubscriptionDeliveriesDefaults(t *testing.T) {
	if got := (events.Subscription{}).Deliveries(); got != events.DefaultMaxDeliveries {
		t.Fatalf("Deliveries() = %d, want default", got)
	}
	if got := (events.Subscription{MaxDeliveries: 2}).Deliveries(); got != 2 {
		t.Fatalf("Deliveries() = %d, want 2", got)
	}
}

func TestSubscriptionDelayRepeatsLastValue(t *testing.T) {
	subscription := events.Subscription{Backoff: []time.Duration{time.Second, 2 * time.Second}}
	expected := map[int]time.Duration{0: 0, 1: time.Second, 2: 2 * time.Second, 5: 2 * time.Second}
	for attempt, want := range expected {
		if got := subscription.Delay(attempt); got != want {
			t.Errorf("Delay(%d) = %v, want %v", attempt, got, want)
		}
	}
	if got := (events.Subscription{}).Delay(3); got != 0 {
		t.Errorf("Delay without backoff = %v, want 0", got)
	}
}

func TestSubscriptionValidate(t *testing.T) {
	valid := events.Subscription{Name: "billing", Subject: "frappe.orders.created.v1"}
	if err := valid.Validate(); err != nil {
		t.Fatalf("Validate: %v", err)
	}
	invalid := map[string]events.Subscription{
		"missing name":     {Subject: "s"},
		"missing subject":  {Name: "n"},
		"negative maximum": {Name: "n", Subject: "s", MaxDeliveries: -1},
		"negative backoff": {Name: "n", Subject: "s", Backoff: []time.Duration{-time.Second}},
	}
	for name, subscription := range invalid {
		t.Run(name, func(t *testing.T) {
			if subscription.Validate() == nil {
				t.Fatal("expected an error")
			}
		})
	}
}

func TestPermanent(t *testing.T) {
	cause := errors.New("undecodable")
	wrapped := fmt.Errorf("handle: %w", events.Permanent(cause))
	if !events.IsPermanent(wrapped) || !errors.Is(wrapped, cause) {
		t.Fatal("Permanent must be detectable and keep its cause")
	}
	if events.IsPermanent(cause) {
		t.Fatal("plain errors are not permanent")
	}
	if events.Permanent(nil) != nil {
		t.Fatal("Permanent(nil) must be nil")
	}
}
