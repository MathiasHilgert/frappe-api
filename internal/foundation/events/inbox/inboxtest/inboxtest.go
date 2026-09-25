// Package inboxtest is the contract test suite every inbox.Store adapter must
// pass. An adapter test calls Run with a factory returning a fresh, empty
// store:
//
//	func TestContract(t *testing.T) {
//		inboxtest.Run(t, func(*testing.T) inbox.Store { return memory.NewStore() })
//	}
package inboxtest

import (
	"context"
	"errors"
	"maps"
	"slices"
	"sync"
	"sync/atomic"
	"testing"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/events/inbox"
)

// Factory returns a fresh, empty store. It is called once per contract case.
type Factory func(t *testing.T) inbox.Store

// Run executes the contract suite against the store built by factory.
func Run(t *testing.T, factory Factory) {
	t.Helper()
	cases := map[string]func(*testing.T, inbox.Store){
		"runs work once and skips duplicates":            testSkipsDuplicates,
		"deduplicates per consumer":                      testPerConsumer,
		"deduplicates per event":                         testPerEvent,
		"failed work is not recorded and can be retried": testFailedWork,
		"concurrent deliveries run work once":            testConcurrentDeliveries,
		"purge forgets only old records":                 testPurge,
	}
	for _, name := range slices.Sorted(maps.Keys(cases)) {
		t.Run(name, func(t *testing.T) {
			t.Parallel()
			cases[name](t, factory(t))
		})
	}
}

// process runs Process and returns whether work ran.
func process(t *testing.T, store inbox.Store, consumer, eventID string) bool {
	t.Helper()
	ran := false
	err := store.Process(context.Background(), consumer, eventID, func(context.Context) error {
		ran = true
		return nil
	})
	if err != nil {
		t.Fatalf("Process(%s, %s): %v", consumer, eventID, err)
	}
	return ran
}

func testSkipsDuplicates(t *testing.T, store inbox.Store) {
	if !process(t, store, "billing-orders-created-v1", "event-1") {
		t.Fatal("work did not run on the first delivery")
	}
	if process(t, store, "billing-orders-created-v1", "event-1") {
		t.Fatal("work ran again on a duplicate delivery")
	}
}

func testPerConsumer(t *testing.T, store inbox.Store) {
	process(t, store, "billing-orders-created-v1", "event-1")
	if !process(t, store, "shipping-orders-created-v1", "event-1") {
		t.Fatal("another consumer's record suppressed the delivery")
	}
}

func testPerEvent(t *testing.T, store inbox.Store) {
	process(t, store, "billing-orders-created-v1", "event-1")
	if !process(t, store, "billing-orders-created-v1", "event-2") {
		t.Fatal("another event's record suppressed the delivery")
	}
}

func testFailedWork(t *testing.T, store inbox.Store) {
	failure := errors.New("handler failed")
	err := store.Process(context.Background(), "billing-orders-created-v1", "event-1", func(context.Context) error {
		return failure
	})
	if !errors.Is(err, failure) {
		t.Fatalf("Process = %v, want %v", err, failure)
	}
	if !process(t, store, "billing-orders-created-v1", "event-1") {
		t.Fatal("work did not run again after a failed attempt")
	}
}

func testConcurrentDeliveries(t *testing.T, store inbox.Store) {
	const deliveries = 8
	var runs atomic.Int32
	var group sync.WaitGroup
	for range deliveries {
		group.Go(func() {
			err := store.Process(context.Background(), "billing-orders-created-v1", "event-1", func(context.Context) error {
				runs.Add(1)
				time.Sleep(20 * time.Millisecond)
				return nil
			})
			if err != nil {
				t.Errorf("Process: %v", err)
			}
		})
	}
	group.Wait()
	if got := runs.Load(); got != 1 {
		t.Fatalf("work ran %d times for %d concurrent deliveries, want 1", got, deliveries)
	}
}

func testPurge(t *testing.T, store inbox.Store) {
	process(t, store, "billing-orders-created-v1", "old")
	// The Postgres store keeps time on the database clock; leave a margin
	// so the cutoff lies strictly between the two records on any clock.
	time.Sleep(300 * time.Millisecond)
	cutoff := time.Now().Add(-100 * time.Millisecond)
	process(t, store, "billing-orders-created-v1", "recent")

	purged, err := store.Purge(context.Background(), cutoff)
	if err != nil {
		t.Fatalf("Purge: %v", err)
	}
	if purged != 1 {
		t.Fatalf("Purge deleted %d records, want 1", purged)
	}
	if !process(t, store, "billing-orders-created-v1", "old") {
		t.Fatal("a purged record still suppressed the delivery")
	}
	if process(t, store, "billing-orders-created-v1", "recent") {
		t.Fatal("a recent record was purged")
	}
}
