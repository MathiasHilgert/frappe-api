// Package memory is an in-memory outbox.Store for tests and for running the
// application without a database. Within provides the ambient unit of work
// Append must join: appends are buffered in the context and become visible
// only when the unit of work commits.
package memory

import (
	"context"
	"errors"
	"maps"
	"slices"
	"sync"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/events"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events/outbox"
)

// ErrNoUnitOfWork is returned by Append when ctx carries no unit of work of
// this store.
var ErrNoUnitOfWork = errors.New("memory outbox: Append requires a unit of work opened with Within")

// Store is the in-memory outbox store. It is safe for concurrent use.
type Store struct {
	notifications chan struct{}
	records       []*record
	mutex         sync.Mutex
}

// record is one stored message and its delivery state.
type record struct {
	leasedUntil time.Time
	retryAt     time.Time
	publishedAt time.Time
	pending     outbox.Pending
	published   bool
}

// unitOfWork buffers the appends of one Within call.
type unitOfWork struct {
	store    *Store
	messages []events.Message
}

type unitOfWorkKey struct{}

// NewStore returns an empty store.
func NewStore() *Store {
	return &Store{notifications: make(chan struct{}, 1)}
}

// Within runs function in a unit of work: messages appended with the context
// passed to function are stored only when function returns nil. A nested
// Within joins the outer unit of work.
func (store *Store) Within(ctx context.Context, function func(ctx context.Context) error) error {
	if current, ok := ctx.Value(unitOfWorkKey{}).(*unitOfWork); ok && current.store == store {
		return function(ctx)
	}
	work := &unitOfWork{store: store}
	if err := function(context.WithValue(ctx, unitOfWorkKey{}, work)); err != nil {
		return err
	}
	store.commit(work.messages)
	return nil
}

func (store *Store) commit(messages []events.Message) {
	if len(messages) == 0 {
		return
	}
	store.mutex.Lock()
	now := time.Now()
	for _, message := range messages {
		store.records = append(store.records, &record{pending: outbox.Pending{Message: clone(message), CreatedAt: now}})
	}
	store.mutex.Unlock()
	select {
	case store.notifications <- struct{}{}:
	default:
	}
}

// Append implements outbox.Store.
func (store *Store) Append(ctx context.Context, messages ...events.Message) error {
	work, ok := ctx.Value(unitOfWorkKey{}).(*unitOfWork)
	if !ok || work.store != store {
		return ErrNoUnitOfWork
	}
	for _, message := range messages {
		work.messages = append(work.messages, clone(message))
	}
	return nil
}

// Claim implements outbox.Store.
func (store *Store) Claim(_ context.Context, limit int, lease time.Duration) ([]outbox.Pending, error) {
	store.mutex.Lock()
	defer store.mutex.Unlock()
	now := time.Now()
	var claimed []outbox.Pending
	for _, current := range store.records {
		if len(claimed) >= limit {
			break
		}
		if current.published || now.Before(current.retryAt) || now.Before(current.leasedUntil) {
			continue
		}
		current.leasedUntil = now.Add(lease)
		claimed = append(claimed, copyPending(current.pending))
	}
	return claimed, nil
}

// MarkPublished implements outbox.Store.
func (store *Store) MarkPublished(_ context.Context, ids ...string) error {
	store.mutex.Lock()
	defer store.mutex.Unlock()
	now := time.Now()
	for _, current := range store.records {
		if slices.Contains(ids, current.pending.Message.ID) {
			current.published = true
			current.publishedAt = now
			current.leasedUntil = time.Time{}
		}
	}
	return nil
}

// MarkFailed implements outbox.Store.
func (store *Store) MarkFailed(_ context.Context, id string, cause string, retryAt time.Time) error {
	store.mutex.Lock()
	defer store.mutex.Unlock()
	for _, current := range store.records {
		if current.pending.Message.ID == id && !current.published {
			current.pending.Attempts++
			current.pending.LastError = cause
			current.retryAt = retryAt
			current.leasedUntil = time.Time{}
		}
	}
	return nil
}

// Purge implements outbox.Store.
func (store *Store) Purge(_ context.Context, publishedBefore time.Time) (int, error) {
	store.mutex.Lock()
	defer store.mutex.Unlock()
	kept := store.records[:0]
	purged := 0
	for _, current := range store.records {
		if current.published && current.publishedAt.Before(publishedBefore) {
			purged++
			continue
		}
		kept = append(kept, current)
	}
	clear(store.records[len(kept):])
	store.records = kept
	return purged, nil
}

// Notifications implements outbox.Store.
func (store *Store) Notifications() <-chan struct{} {
	return store.notifications
}

// OldestPending implements outbox.LagReporter.
func (store *Store) OldestPending(context.Context) (time.Time, bool, error) {
	store.mutex.Lock()
	defer store.mutex.Unlock()
	for _, current := range store.records {
		if !current.published {
			return current.pending.CreatedAt, true, nil
		}
	}
	return time.Time{}, false, nil
}

func copyPending(pending outbox.Pending) outbox.Pending {
	pending.Message = clone(pending.Message)
	return pending
}

func clone(message events.Message) events.Message {
	message.Payload = slices.Clone(message.Payload)
	message.Headers = maps.Clone(message.Headers)
	return message
}
