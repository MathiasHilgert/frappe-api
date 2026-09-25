package eventinvalidation_test

import (
	"context"
	"errors"
	"sync/atomic"
	"testing"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/cache"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/cache/cachetest"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/cache/eventinvalidation"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/cache/memory"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events"
)

type menuUpdated struct {
	Tenant string `json:"tenant"`
	MenuID string `json:"menu_id"`
}

var updated = events.Define[menuUpdated]("invalidationtest.updated", 1)

var entryCounter atomic.Int64

func TestOnInvalidatesTheEventsKeys(t *testing.T) {
	var loads atomic.Int64
	backend := cache.NewBackend(memory.NewStore(), cache.Settings{TenantResolver: cachetest.TenantResolver})
	name := "invalidationtest.by_id_" + string(rune('a'+entryCounter.Add(1)))
	byID := cache.New(backend, name, time.Minute, func(context.Context, string) (int64, error) {
		return loads.Add(1), nil
	})
	registry := events.NewRegistry()
	eventinvalidation.On(registry.Module("menu"), updated, byID,
		func(event events.Event[menuUpdated]) (string, []string) {
			return event.Data.Tenant, []string{event.Data.MenuID}
		})

	ctx := cachetest.WithTenant(context.Background(), "acme")
	if _, err := byID.Get(ctx, "m1"); err != nil {
		t.Fatalf("Get returned error: %v", err)
	}
	registrations := registry.Registrations()
	if len(registrations) != 1 || registrations[0].Subscription.Subject != updated.Type() {
		t.Fatalf("registrations = %+v; want one on %s", registrations, updated.Type())
	}
	message, err := events.NewMessage(context.Background(), updated.With(menuUpdated{Tenant: "acme", MenuID: "m1"}))
	if err != nil {
		t.Fatal(err)
	}
	if err := registrations[0].Handler(context.Background(), message); err != nil {
		t.Fatalf("handler returned error: %v", err)
	}

	if value, err := byID.Get(ctx, "m1"); err != nil || value != 2 {
		t.Fatalf("Get after the event = %d, %v; want a reload (2)", value, err)
	}
}

type failingInvalidator struct{}

func (failingInvalidator) Name() string { return "invalidationtest.failing" }

func (failingInvalidator) InvalidateFor(context.Context, string, ...string) error {
	return errors.New("store down")
}

func TestOnReturnsInvalidationFailuresForRedelivery(t *testing.T) {
	registry := events.NewRegistry()
	eventinvalidation.On(registry.Module("menu"), updated, failingInvalidator{},
		func(event events.Event[menuUpdated]) (string, []string) {
			return event.Data.Tenant, []string{event.Data.MenuID}
		})
	message, err := events.NewMessage(context.Background(), updated.With(menuUpdated{Tenant: "acme", MenuID: "m1"}))
	if err != nil {
		t.Fatal(err)
	}
	if err := registry.Registrations()[0].Handler(context.Background(), message); err == nil {
		t.Fatal("handler returned nil error although invalidation failed")
	}
}
