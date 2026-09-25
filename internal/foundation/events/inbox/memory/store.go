// Package memory is an in-memory inbox.Store for tests and for running the
// application without a database. Records live only in memory and are lost
// when the process stops, and work runs in no unit of work of its own: its
// effects are not rolled back with the record.
package memory

import (
	"context"
	"sync"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/events/inbox"
)

// Statically assert Store satisfies inbox.Store.
var _ inbox.Store = (*Store)(nil)

// key identifies one processed event of one consumer.
type key struct {
	consumer string
	eventID  string
}

// Store is the in-memory inbox store. It is safe for concurrent use.
type Store struct {
	processed map[key]time.Time
	locks     map[key]*sync.Mutex
	mutex     sync.Mutex
}

// NewStore returns an empty store.
func NewStore() *Store {
	return &Store{processed: map[key]time.Time{}, locks: map[key]*sync.Mutex{}}
}

// Process implements inbox.Store. Concurrent calls for the same pair are
// serialized, like a unique index serializes concurrent inserts.
func (store *Store) Process(ctx context.Context, consumer, eventID string, work func(ctx context.Context) error) error {
	pair := key{consumer: consumer, eventID: eventID}
	lock := store.lock(pair)
	lock.Lock()
	defer lock.Unlock()

	if store.recorded(pair) {
		return nil
	}
	if err := work(ctx); err != nil {
		return err
	}
	store.mutex.Lock()
	store.processed[pair] = time.Now()
	store.mutex.Unlock()
	return nil
}

// Purge implements inbox.Store.
func (store *Store) Purge(_ context.Context, processedBefore time.Time) (int, error) {
	store.mutex.Lock()
	defer store.mutex.Unlock()
	purged := 0
	for pair, processedAt := range store.processed {
		if processedAt.Before(processedBefore) {
			delete(store.processed, pair)
			delete(store.locks, pair)
			purged++
		}
	}
	return purged, nil
}

func (store *Store) lock(pair key) *sync.Mutex {
	store.mutex.Lock()
	defer store.mutex.Unlock()
	lock, ok := store.locks[pair]
	if !ok {
		lock = &sync.Mutex{}
		store.locks[pair] = lock
	}
	return lock
}

func (store *Store) recorded(pair key) bool {
	store.mutex.Lock()
	defer store.mutex.Unlock()
	_, ok := store.processed[pair]
	return ok
}
