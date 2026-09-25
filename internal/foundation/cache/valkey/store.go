// Package valkey is the cache.Store backed by Valkey. It reuses the shared
// client built by internal/foundation/valkey, stores values with SET PX and
// deletes keys in pipelined batches of single-key DEL commands, which stays
// correct on a clustered deployment where one multi-key DEL could span hash
// slots.
package valkey

import (
	"context"
	"errors"
	"fmt"
	"time"

	valkeygo "github.com/valkey-io/valkey-go"
)

// deleteBatchSize bounds how many DEL commands one pipeline carries.
const deleteBatchSize = 500

// Settings configures a Store.
type Settings struct {
	// KeyPrefix is prepended to every key. Optional: keys built by
	// cache.Define already start with "frappe:".
	KeyPrefix string
}

// Store is a cache.Store on Valkey. It is safe for concurrent use.
type Store struct {
	client    valkeygo.Client
	keyPrefix string
}

// NewStore returns a Store over client.
func NewStore(client valkeygo.Client, settings Settings) *Store {
	return &Store{client: client, keyPrefix: settings.KeyPrefix}
}

// Get implements cache.Store.
func (store *Store) Get(ctx context.Context, key string) ([]byte, bool, error) {
	value, err := store.client.Do(ctx, store.client.B().Get().Key(store.keyPrefix+key).Build()).AsBytes()
	if valkeygo.IsValkeyNil(err) {
		return nil, false, nil
	}
	if err != nil {
		return nil, false, fmt.Errorf("cache valkey: get: %w", err)
	}
	if value == nil {
		value = []byte{}
	}
	return value, true, nil
}

// Set implements cache.Store.
func (store *Store) Set(ctx context.Context, key string, value []byte, timeToLive time.Duration) error {
	milliseconds := timeToLive.Milliseconds()
	if timeToLive <= 0 {
		return fmt.Errorf("cache valkey: time to live must be positive, got %s", timeToLive)
	}
	milliseconds = max(milliseconds, 1)
	command := store.client.B().Set().Key(store.keyPrefix + key).Value(valkeygo.BinaryString(value)).PxMilliseconds(milliseconds).Build()
	if err := store.client.Do(ctx, command).Error(); err != nil {
		return fmt.Errorf("cache valkey: set: %w", err)
	}
	return nil
}

// Delete implements cache.Store.
func (store *Store) Delete(ctx context.Context, keys ...string) error {
	for start := 0; start < len(keys); start += deleteBatchSize {
		batch := keys[start:min(start+deleteBatchSize, len(keys))]
		commands := make(valkeygo.Commands, 0, len(batch))
		for _, key := range batch {
			commands = append(commands, store.client.B().Del().Key(store.keyPrefix+key).Build())
		}
		var failures []error
		for _, result := range store.client.DoMulti(ctx, commands...) {
			if err := result.Error(); err != nil {
				failures = append(failures, err)
			}
		}
		if len(failures) > 0 {
			return fmt.Errorf("cache valkey: delete: %w", errors.Join(failures...))
		}
	}
	return nil
}
