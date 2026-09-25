// Package cachetest is the contract test suite every cache.Store adapter
// must pass. An adapter test calls Run with a factory returning a fresh,
// empty store:
//
//	func TestContract(t *testing.T) {
//		cachetest.Run(t, func(*testing.T) cache.Store { return memory.NewStore() })
//	}
//
// Expiry assertions use short lifetimes with a generous upper bound, so the
// suite stays stable on slow CI machines.
package cachetest

import (
	"bytes"
	"context"
	"fmt"
	"maps"
	"slices"
	"sync"
	"testing"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/cache"
)

// Factory returns a fresh, empty Store. It is called once per contract case.
type Factory func(t *testing.T) cache.Store

// eventually bounds every expiry expectation.
const eventually = 5 * time.Second

// Run executes the contract suite against the store built by factory.
func Run(t *testing.T, factory Factory) {
	t.Helper()
	cases := map[string]func(*testing.T, cache.Store){
		"a missing key misses":                   testMissingKey,
		"returns what was set":                   testSetAndGet,
		"set replaces the previous value":        testReplace,
		"values are binary safe":                 testBinarySafety,
		"an empty value is still a hit":          testEmptyValue,
		"entries expire after their lifetime":    testExpiry,
		"rejects a non-positive lifetime":        testNonPositiveLifetime,
		"deletes several keys, ignoring missing": testDelete,
		"deleting nothing is a no-op":            testDeleteNothing,
		"returned values are copies":             testReturnedCopy,
		"is safe for concurrent use":             testConcurrency,
	}
	for _, name := range slices.Sorted(maps.Keys(cases)) {
		t.Run(name, func(t *testing.T) {
			t.Parallel()
			cases[name](t, factory(t))
		})
	}
}

// uniqueKey keeps cases independent even when a factory hands every case
// the same backing server.
func uniqueKey(t *testing.T, suffix string) string {
	return fmt.Sprintf("frappe:global:cachetest:%s:%s:%d", t.Name(), suffix, time.Now().UnixNano())
}

func mustSet(t *testing.T, store cache.Store, key string, value []byte) {
	t.Helper()
	if err := store.Set(context.Background(), key, value, time.Minute); err != nil {
		t.Fatalf("Set(%q) returned error: %v", key, err)
	}
}

func mustGet(t *testing.T, store cache.Store, key string) ([]byte, bool) {
	t.Helper()
	value, found, err := store.Get(context.Background(), key)
	if err != nil {
		t.Fatalf("Get(%q) returned error: %v", key, err)
	}
	return value, found
}

func testMissingKey(t *testing.T, store cache.Store) {
	if value, found := mustGet(t, store, uniqueKey(t, "missing")); found || value != nil {
		t.Fatalf("Get on a missing key = %q, %v; want nil, false", value, found)
	}
}

func testSetAndGet(t *testing.T, store cache.Store) {
	key := uniqueKey(t, "value")
	mustSet(t, store, key, []byte("hello"))
	if value, found := mustGet(t, store, key); !found || string(value) != "hello" {
		t.Fatalf("Get = %q, %v; want hello, true", value, found)
	}
}

func testReplace(t *testing.T, store cache.Store) {
	key := uniqueKey(t, "value")
	mustSet(t, store, key, []byte("first"))
	mustSet(t, store, key, []byte("second"))
	if value, _ := mustGet(t, store, key); string(value) != "second" {
		t.Fatalf("Get = %q; want second", value)
	}
}

func testBinarySafety(t *testing.T, store cache.Store) {
	key := uniqueKey(t, "binary")
	value := make([]byte, 0, 512)
	for index := range 512 {
		value = append(value, byte(index%256))
	}
	mustSet(t, store, key, value)
	if got, found := mustGet(t, store, key); !found || !bytes.Equal(got, value) {
		t.Fatalf("Get returned %d bytes (found %v); want the 512 bytes set", len(got), found)
	}
}

func testEmptyValue(t *testing.T, store cache.Store) {
	key := uniqueKey(t, "empty")
	mustSet(t, store, key, []byte{})
	if value, found := mustGet(t, store, key); !found || len(value) != 0 {
		t.Fatalf("Get = %q, %v; want empty, true", value, found)
	}
}

func testExpiry(t *testing.T, store cache.Store) {
	key := uniqueKey(t, "expiry")
	if err := store.Set(context.Background(), key, []byte("short"), 100*time.Millisecond); err != nil {
		t.Fatalf("Set returned error: %v", err)
	}
	if _, found := mustGet(t, store, key); !found {
		t.Fatal("entry missing right after Set")
	}
	deadline := time.Now().Add(eventually)
	for time.Now().Before(deadline) {
		if _, found := mustGet(t, store, key); !found {
			return
		}
		time.Sleep(20 * time.Millisecond)
	}
	t.Fatalf("entry still present %s after its 100ms lifetime", eventually)
}

func testNonPositiveLifetime(t *testing.T, store cache.Store) {
	key := uniqueKey(t, "lifetime")
	for _, timeToLive := range []time.Duration{0, -time.Second} {
		if err := store.Set(context.Background(), key, []byte("x"), timeToLive); err == nil {
			t.Fatalf("Set with lifetime %s returned nil error", timeToLive)
		}
	}
	if _, found := mustGet(t, store, key); found {
		t.Fatal("a rejected Set stored a value")
	}
}

func testDelete(t *testing.T, store cache.Store) {
	first, second, kept := uniqueKey(t, "first"), uniqueKey(t, "second"), uniqueKey(t, "kept")
	for _, key := range []string{first, second, kept} {
		mustSet(t, store, key, []byte(key))
	}
	if err := store.Delete(context.Background(), first, second, uniqueKey(t, "missing")); err != nil {
		t.Fatalf("Delete returned error: %v", err)
	}
	for _, key := range []string{first, second} {
		if _, found := mustGet(t, store, key); found {
			t.Fatalf("%q still present after Delete", key)
		}
	}
	if _, found := mustGet(t, store, kept); !found {
		t.Fatal("Delete removed a key it was not given")
	}
}

func testDeleteNothing(t *testing.T, store cache.Store) {
	if err := store.Delete(context.Background()); err != nil {
		t.Fatalf("Delete with no keys returned error: %v", err)
	}
}

func testReturnedCopy(t *testing.T, store cache.Store) {
	key := uniqueKey(t, "copy")
	original := []byte("immutable")
	mustSet(t, store, key, original)
	original[0] = 'X'
	value, _ := mustGet(t, store, key)
	value[1] = 'Y'
	if again, _ := mustGet(t, store, key); string(again) != "immutable" {
		t.Fatalf("stored value changed to %q through a caller's slice", again)
	}
}

func testConcurrency(t *testing.T, store cache.Store) {
	key := uniqueKey(t, "shared")
	var group sync.WaitGroup
	for worker := range 16 {
		group.Go(func() {
			own := fmt.Sprintf("%s:%d", key, worker)
			for range 50 {
				if err := exerciseOnce(store, own, key); err != nil {
					t.Error(err)
					return
				}
			}
		})
	}
	group.Wait()
}

// exerciseOnce writes and reads a worker's own key, then races every other
// worker on the shared key.
func exerciseOnce(store cache.Store, own, shared string) error {
	ctx := context.Background()
	if err := store.Set(ctx, own, []byte(own), time.Minute); err != nil {
		return fmt.Errorf("concurrent Set returned error: %w", err)
	}
	value, found, err := store.Get(ctx, own)
	if err != nil || !found || string(value) != own {
		return fmt.Errorf("concurrent Get = %q, %v, %w; want %q", value, found, err, own)
	}
	if err := store.Set(ctx, shared, []byte("shared"), time.Minute); err != nil {
		return fmt.Errorf("concurrent Set on a shared key returned error: %w", err)
	}
	if err := store.Delete(ctx, shared); err != nil {
		return fmt.Errorf("concurrent Delete returned error: %w", err)
	}
	return nil
}
