// Package outbox implements the transactional outbox pattern on top of the
// broker-agnostic events ports, independently of any storage technology.
//
// A use case records events through Recorder, which appends them to a Store
// inside the caller's unit of work. Relay then claims pending messages, hands
// them to an events.Publisher and marks them published or failed. Storage
// adapters (Postgres in production, memory for tests) implement Store and
// prove it with the storetest contract suite.
package outbox

import (
	"context"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/events"
)

// Pending is a stored message waiting to be published.
type Pending struct {
	// CreatedAt is when the message was appended.
	CreatedAt time.Time
	// LastError is the cause recorded by the last MarkFailed, if any.
	LastError string
	// Message is the message exactly as it was appended.
	Message events.Message
	// Attempts is the number of failed publish attempts so far.
	Attempts int
}

// Store persists outbox messages.
//
// Atomicity with business writes is the whole point of an outbox: Append
// MUST join the caller's ambient unit of work carried by ctx (for example the
// database transaction started by the use case), so the messages become
// visible if and only if that unit of work commits. This only holds when the
// Store shares the business datastore and transaction; an adapter must fail
// Append rather than write outside the caller's unit of work.
//
// Every method other than Append runs in its own unit of work, and every
// method must be safe for concurrent use by several relay replicas.
type Store interface {
	// Append stores messages in the caller's unit of work. Appending no
	// messages is a no-op.
	Append(ctx context.Context, messages ...events.Message) error
	// Claim leases up to limit pending messages for lease and returns them
	// oldest first. A message is pending when it is not published, its
	// retry time has passed and it is not leased. A claimed message is
	// invisible to every other Claim, including from other replicas, until
	// the lease expires or it is marked published or failed; an expired
	// lease makes it claimable again, which is what makes publishing
	// at-least-once when a relay crashes.
	Claim(ctx context.Context, limit int, lease time.Duration) ([]Pending, error)
	// MarkPublished marks messages as published; they are never claimed
	// again. Unknown IDs are ignored.
	MarkPublished(ctx context.Context, ids ...string) error
	// MarkFailed releases the lease of message id, increments its attempts,
	// records cause and makes it claimable again at retryAt.
	MarkFailed(ctx context.Context, id string, cause string, retryAt time.Time) error
	// Purge deletes messages published before publishedBefore and returns
	// how many were deleted. Unpublished messages are never purged.
	Purge(ctx context.Context, publishedBefore time.Time) (int, error)
	// Notifications returns a channel that receives a value, as a best
	// effort wake-up hint, after appended messages are committed; the relay
	// still polls, so hints may be coalesced or lost. It returns nil when the
	// store does not support notifications.
	Notifications() <-chan struct{}
}

// LagReporter is optionally implemented by a Store to expose the age of its
// backlog, reported by Relay as the frappe.outbox.lag gauge.
type LagReporter interface {
	// OldestPending returns the creation time of the oldest unpublished
	// message, and false when there is none.
	OldestPending(ctx context.Context) (time.Time, bool, error)
}
