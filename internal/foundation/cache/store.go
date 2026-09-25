package cache

import (
	"context"
	"time"
)

// Store is the storage port behind every Cache: a byte-oriented key value
// store with per-entry expiry. Adapters live in subpackages (memory,
// valkey) and must pass the cachetest contract suite.
//
// Keys are opaque strings built by Definition; values are opaque bytes and
// must be stored and returned unchanged, including zero bytes and invalid
// UTF-8. Implementations must be safe for concurrent use.
type Store interface {
	// Get returns the value stored under key and true, or false when the
	// key is missing or expired. The returned slice is owned by the caller.
	Get(ctx context.Context, key string) ([]byte, bool, error)
	// Set stores value under key for timeToLive, replacing any previous
	// value. timeToLive must be positive.
	Set(ctx context.Context, key string, value []byte, timeToLive time.Duration) error
	// Delete removes keys; missing keys are ignored and no keys is a no-op.
	Delete(ctx context.Context, keys ...string) error
}

// DisabledStore is the Store used while caching is disabled: it never
// stores anything, so every Get misses and every Definition simply loads.
// Decorators keep working unchanged when the cache is turned off.
type DisabledStore struct{}

// Get implements Store and always misses.
func (DisabledStore) Get(context.Context, string) ([]byte, bool, error) {
	return nil, false, nil
}

// Set implements Store and discards the value.
func (DisabledStore) Set(context.Context, string, []byte, time.Duration) error {
	return nil
}

// Delete implements Store and does nothing.
func (DisabledStore) Delete(context.Context, ...string) error {
	return nil
}
