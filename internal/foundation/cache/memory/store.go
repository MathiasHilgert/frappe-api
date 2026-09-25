// Package memory is an in-process cache.Store for tests and single-process
// local runs. Entries live in one map guarded by a mutex; expired entries
// are dropped lazily when read and swept on writes, so memory stays bounded
// by the live entries plus whatever expired since the last write.
package memory

import (
	"context"
	"fmt"
	"sync"
	"time"
)

// sweepEvery is how many Set calls pass between two sweeps of expired
// entries.
const sweepEvery = 1024

type entry struct {
	expiresAt time.Time
	value     []byte
}

// Store is an in-memory cache.Store. The zero value is not usable; call
// NewStore.
type Store struct {
	entries map[string]entry
	writes  int
	mutex   sync.Mutex
}

// NewStore returns an empty Store.
func NewStore() *Store {
	return &Store{entries: map[string]entry{}}
}

// Get implements cache.Store.
func (store *Store) Get(_ context.Context, key string) ([]byte, bool, error) {
	store.mutex.Lock()
	defer store.mutex.Unlock()
	stored, found := store.entries[key]
	if !found {
		return nil, false, nil
	}
	if !time.Now().Before(stored.expiresAt) {
		delete(store.entries, key)
		return nil, false, nil
	}
	return append([]byte{}, stored.value...), true, nil
}

// Set implements cache.Store.
func (store *Store) Set(_ context.Context, key string, value []byte, timeToLive time.Duration) error {
	if timeToLive <= 0 {
		return fmt.Errorf("cache memory: time to live must be positive, got %s", timeToLive)
	}
	store.mutex.Lock()
	defer store.mutex.Unlock()
	store.entries[key] = entry{value: append([]byte{}, value...), expiresAt: time.Now().Add(timeToLive)}
	store.writes++
	if store.writes%sweepEvery == 0 {
		store.sweep()
	}
	return nil
}

// Delete implements cache.Store.
func (store *Store) Delete(_ context.Context, keys ...string) error {
	store.mutex.Lock()
	defer store.mutex.Unlock()
	for _, key := range keys {
		delete(store.entries, key)
	}
	return nil
}

// sweep drops every expired entry. The caller holds the mutex.
func (store *Store) sweep() {
	now := time.Now()
	for key, stored := range store.entries {
		if !now.Before(stored.expiresAt) {
			delete(store.entries, key)
		}
	}
}
