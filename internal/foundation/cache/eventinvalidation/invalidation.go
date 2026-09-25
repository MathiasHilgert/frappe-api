// Package eventinvalidation deletes cache entries when events arrive,
// without coupling the cache to the event platform.
//
// The producing module records an event (for example menu.updated)
// through the transactional outbox in the same transaction as the change;
// the relay publishes it, and the handler registered by On deletes the
// affected entries on every replica's shared store. Entry lifetimes still
// bound staleness if an event is delayed.
//
//	eventinvalidation.On(registry, menuevents.Updated, byID,
//		func(event events.Event[menuevents.MenuUpdated]) (string, []uuid.UUID) {
//			return event.Data.Tenant, []uuid.UUID{event.Data.MenuID}
//		})
package eventinvalidation

import (
	"context"
	"strings"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/cache"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events"
)

// Invalidator is what On needs from a cache entry; *cache.ReadThrough
// implements it.
type Invalidator[K cache.Key] interface {
	Name() string
	InvalidateFor(ctx context.Context, tenant string, keys ...K) error
}

// On registers, on registry (a module view), a handler for definition that
// deletes the entries keys returns. keys returns the tenant taken from the
// event ("" for a Global entry) and the keys to delete. A failed deletion
// is returned so the event is redelivered. The durable consumer name gets
// "cache_<entry name>" added, so it never collides with the module's other
// handlers for the same event.
func On[T any, K cache.Key](registry *events.Registry, definition events.Definition[T], entry Invalidator[K], keys func(events.Event[T]) (string, []K), options ...events.Option) {
	name := events.Named("cache_" + strings.ReplaceAll(entry.Name(), ".", "_"))
	events.On(registry, definition, func(ctx context.Context, event events.Event[T]) error {
		tenant, affected := keys(event)
		return entry.InvalidateFor(ctx, tenant, affected...)
	}, append([]events.Option{name}, options...)...)
}
