// Package inbox makes event consumers idempotent, independently of any
// storage technology.
//
// Brokers deliver every message at least once, so a handler can see the same
// event more than once (a relay republishing after a crash, a redelivery
// after a lost acknowledgement). A Store remembers which (consumer, event ID)
// pairs were processed and runs a handler's work only for the first one, in
// the same unit of work that records the pair, so the handler's effects and
// the record commit or roll back together: handlers become exactly-once in
// effect. Consumers subscribes every registered subscription and wraps its
// handler with a Store. Storage adapters (Postgres in production, memory for
// tests) implement Store and prove it with the inboxtest contract suite.
package inbox

import (
	"context"
	"time"
)

// Store records processed events per consumer.
type Store interface {
	// Process runs work at most once per (consumer, eventID). Within one
	// unit of work it records the pair and runs work with a ctx carrying
	// that unit of work (for example the database transaction), so work's
	// own writes commit with the record. When the pair is already recorded
	// it skips work and returns nil: the delivery is a duplicate. When work
	// fails, nothing is recorded and its error is returned, so a redelivery
	// runs work again. Concurrent calls for the same pair run work once.
	Process(ctx context.Context, consumer, eventID string, work func(ctx context.Context) error) error
	// Purge forgets records processed before processedBefore and returns
	// how many were deleted. A delivery of a purged event runs work again,
	// so the retention must outlive every possible redelivery.
	Purge(ctx context.Context, processedBefore time.Time) (int, error)
}
