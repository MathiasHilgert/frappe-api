// Package storetest is the contract test suite every outbox.Store adapter must
// pass. An adapter test calls Run with a factory returning a fresh, empty
// store together with the way to open a unit of work on it:
//
//	func TestContract(t *testing.T) {
//		storetest.Run(t, func(t *testing.T) storetest.Subject {
//			store := memory.NewStore()
//			return storetest.Subject{Store: store, Within: store.Within}
//		})
//	}
//
// Timing assertions only check lower bounds plus a generous upper bound, so
// the suite stays stable on slow CI machines.
package storetest

import (
	"context"
	"errors"
	"fmt"
	"maps"
	"slices"
	"sync"
	"testing"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/events"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events/outbox"
)

// Subject is the store under test.
type Subject struct {
	// Store is a fresh, empty store.
	Store outbox.Store
	// Within runs function in a new unit of work on Store (for example a
	// database transaction), committing when it returns nil and rolling
	// back otherwise. Append is only ever called inside Within.
	Within func(ctx context.Context, function func(ctx context.Context) error) error
}

// Factory returns a fresh Subject. It is called once per contract case.
type Factory func(t *testing.T) Subject

// eventually bounds every asynchronous expectation.
const eventually = 5 * time.Second

// Run executes the contract suite against the store built by factory.
func Run(t *testing.T, factory Factory) {
	t.Helper()
	cases := map[string]func(*testing.T, Subject){
		"claims appended messages oldest first and intact": testAppendAndClaim,
		"appending nothing is a no-op":                     testAppendNothing,
		"discards appends of a rolled back unit of work":   testRollback,
		"claim honors the limit":                           testClaimLimit,
		"claims are exclusive under concurrency":           testClaimExclusivity,
		"an expired lease makes a message claimable again": testLeaseExpiry,
		"published messages are never claimed again":       testMarkPublished,
		"failed messages wait until their retry time":      testMarkFailed,
		"purge deletes only old published messages":        testPurge,
		"notifies after appends are committed":             testNotifications,
		"reports the oldest pending message":               testOldestPending,
	}
	for _, name := range slices.Sorted(maps.Keys(cases)) {
		t.Run(name, func(t *testing.T) {
			t.Parallel()
			cases[name](t, factory(t))
		})
	}
}

func message(index int) events.Message {
	return events.Message{
		ID:      fmt.Sprintf("message-%03d", index),
		Subject: "frappe.storetest.created.v1",
		Payload: fmt.Appendf(nil, `{"index":%d}`, index),
		Headers: map[string]string{"content-type": "application/cloudevents+json"},
	}
}

func appendMessages(t *testing.T, subject Subject, messages ...events.Message) {
	t.Helper()
	err := subject.Within(context.Background(), func(ctx context.Context) error {
		return subject.Store.Append(ctx, messages...)
	})
	if err != nil {
		t.Fatalf("Append: %v", err)
	}
}

func appendRange(t *testing.T, subject Subject, count int) {
	t.Helper()
	for index := range count {
		appendMessages(t, subject, message(index))
	}
}

func claim(t *testing.T, subject Subject, limit int, lease time.Duration) []outbox.Pending {
	t.Helper()
	claimed, err := subject.Store.Claim(context.Background(), limit, lease)
	if err != nil {
		t.Fatalf("Claim: %v", err)
	}
	return claimed
}

func ids(claimed []outbox.Pending) []string {
	result := make([]string, 0, len(claimed))
	for _, pending := range claimed {
		result = append(result, pending.Message.ID)
	}
	return result
}

func waitClaimable(t *testing.T, subject Subject, description string) []outbox.Pending {
	t.Helper()
	deadline := time.Now().Add(eventually)
	for {
		if claimed := claim(t, subject, 10, time.Minute); len(claimed) > 0 {
			return claimed
		}
		if time.Now().After(deadline) {
			t.Fatalf("timed out waiting for %s", description)
		}
		time.Sleep(10 * time.Millisecond)
	}
}

func testAppendAndClaim(t *testing.T, subject Subject) {
	before := time.Now()
	first, second := message(1), message(2)
	appendMessages(t, subject, first, second)
	appendMessages(t, subject, message(3))

	claimed := claim(t, subject, 10, time.Minute)
	if got := ids(claimed); !slices.Equal(got, []string{"message-001", "message-002", "message-003"}) {
		t.Fatalf("claimed %v, want append order", got)
	}
	got := claimed[0]
	if got.Message.ID != first.ID || got.Message.Subject != first.Subject || string(got.Message.Payload) != string(first.Payload) || !maps.Equal(got.Message.Headers, first.Headers) {
		t.Fatalf("claimed message %+v, want %+v", got.Message, first)
	}
	if got.Attempts != 0 || got.LastError != "" {
		t.Fatalf("fresh message has attempts %d, error %q", got.Attempts, got.LastError)
	}
	if got.CreatedAt.Before(before.Add(-time.Second)) || got.CreatedAt.After(time.Now().Add(time.Second)) {
		t.Fatalf("CreatedAt %v is not around the append time %v", got.CreatedAt, before)
	}
}

func testAppendNothing(t *testing.T, subject Subject) {
	appendMessages(t, subject)
	if claimed := claim(t, subject, 10, time.Minute); len(claimed) != 0 {
		t.Fatalf("claimed %v from an empty store", ids(claimed))
	}
}

func testRollback(t *testing.T, subject Subject) {
	failure := errors.New("business rule violated")
	err := subject.Within(context.Background(), func(ctx context.Context) error {
		if err := subject.Store.Append(ctx, message(1)); err != nil {
			return err
		}
		return failure
	})
	if !errors.Is(err, failure) {
		t.Fatalf("Within() = %v, want %v", err, failure)
	}
	if claimed := claim(t, subject, 10, time.Minute); len(claimed) != 0 {
		t.Fatalf("rolled back messages were claimed: %v", ids(claimed))
	}
}

func testClaimLimit(t *testing.T, subject Subject) {
	appendRange(t, subject, 5)
	if got := ids(claim(t, subject, 2, time.Minute)); !slices.Equal(got, []string{"message-000", "message-001"}) {
		t.Fatalf("first claim = %v", got)
	}
	if got := ids(claim(t, subject, 10, time.Minute)); !slices.Equal(got, []string{"message-002", "message-003", "message-004"}) {
		t.Fatalf("second claim = %v", got)
	}
}

func testClaimExclusivity(t *testing.T, subject Subject) {
	const total, workers = 60, 6
	appendRange(t, subject, total)

	var mutex sync.Mutex
	seen := map[string]int{}
	var group sync.WaitGroup
	for range workers {
		group.Go(func() {
			for _, id := range claimUntilEmpty(t, subject) {
				mutex.Lock()
				seen[id]++
				mutex.Unlock()
			}
		})
	}
	group.Wait()

	if len(seen) != total {
		t.Fatalf("claimed %d distinct messages, want %d", len(seen), total)
	}
	for id, count := range seen {
		if count != 1 {
			t.Fatalf("message %s claimed %d times under a live lease", id, count)
		}
	}
}

// claimUntilEmpty claims small batches until the store has nothing left and
// returns every claimed ID. It is called concurrently.
func claimUntilEmpty(t *testing.T, subject Subject) []string {
	var claimedIDs []string
	for {
		claimed, err := subject.Store.Claim(context.Background(), 4, time.Minute)
		if err != nil {
			t.Errorf("Claim: %v", err)
			return claimedIDs
		}
		if len(claimed) == 0 {
			return claimedIDs
		}
		claimedIDs = append(claimedIDs, ids(claimed)...)
	}
}

func testLeaseExpiry(t *testing.T, subject Subject) {
	appendMessages(t, subject, message(1))
	lease := 200 * time.Millisecond
	claimedAt := time.Now()
	if got := ids(claim(t, subject, 10, lease)); len(got) != 1 {
		t.Fatalf("first claim = %v", got)
	}
	if got := ids(claim(t, subject, 10, lease)); len(got) != 0 {
		t.Fatalf("leased message claimed again: %v", got)
	}
	reclaimed := waitClaimable(t, subject, "lease expiry")
	if elapsed := time.Since(claimedAt); elapsed < lease {
		t.Fatalf("reclaimed after %v, before the %v lease expired", elapsed, lease)
	}
	if reclaimed[0].Message.ID != "message-001" {
		t.Fatalf("reclaimed %v", ids(reclaimed))
	}
}

func testMarkPublished(t *testing.T, subject Subject) {
	appendRange(t, subject, 3)
	claimed := claim(t, subject, 10, 50*time.Millisecond)
	if err := subject.Store.MarkPublished(context.Background(), claimed[0].Message.ID, claimed[1].Message.ID, "unknown-id"); err != nil {
		t.Fatalf("MarkPublished: %v", err)
	}
	time.Sleep(150 * time.Millisecond)
	if got := ids(claim(t, subject, 10, time.Minute)); !slices.Equal(got, []string{"message-002"}) {
		t.Fatalf("after publishing two, claimed %v", got)
	}
}

func testMarkFailed(t *testing.T, subject Subject) {
	appendRange(t, subject, 2)
	claimed := claim(t, subject, 10, time.Minute)
	ctx := context.Background()
	delay := 200 * time.Millisecond
	failedAt := time.Now()
	if err := subject.Store.MarkFailed(ctx, claimed[0].Message.ID, "broker unavailable", failedAt.Add(delay)); err != nil {
		t.Fatalf("MarkFailed: %v", err)
	}
	if err := subject.Store.MarkFailed(ctx, claimed[1].Message.ID, "timeout", failedAt.Add(-time.Second)); err != nil {
		t.Fatalf("MarkFailed: %v", err)
	}

	immediate := claim(t, subject, 10, time.Minute)
	if got := ids(immediate); !slices.Equal(got, []string{"message-001"}) {
		t.Fatalf("claimable right after failing = %v, want only the one whose retry time passed", got)
	}
	if immediate[0].Attempts != 1 || immediate[0].LastError != "timeout" {
		t.Fatalf("failed message has attempts %d, error %q", immediate[0].Attempts, immediate[0].LastError)
	}

	retried := waitClaimable(t, subject, "retry time")
	if elapsed := time.Since(failedAt); elapsed < delay {
		t.Fatalf("claimed after %v, before retryAt (%v)", elapsed, delay)
	}
	if retried[0].Message.ID != "message-000" || retried[0].Attempts != 1 || retried[0].LastError != "broker unavailable" {
		t.Fatalf("retried %+v", retried[0])
	}
}

func testPurge(t *testing.T, subject Subject) {
	appendRange(t, subject, 3)
	claimed := claim(t, subject, 2, time.Minute)
	ctx := context.Background()
	if err := subject.Store.MarkPublished(ctx, ids(claimed)...); err != nil {
		t.Fatal(err)
	}

	if purged, err := subject.Store.Purge(ctx, time.Now().Add(-time.Hour)); err != nil || purged != 0 {
		t.Fatalf("Purge(an hour ago) = %d, %v; want 0 recently published", purged, err)
	}
	if purged, err := subject.Store.Purge(ctx, time.Now().Add(time.Hour)); err != nil || purged != 2 {
		t.Fatalf("Purge(an hour ahead) = %d, %v; want 2", purged, err)
	}
	if got := ids(claim(t, subject, 10, time.Minute)); !slices.Equal(got, []string{"message-002"}) {
		t.Fatalf("unpublished message lost by purge, claimed %v", got)
	}
}

func testNotifications(t *testing.T, subject Subject) {
	notifications := subject.Store.Notifications()
	if notifications == nil {
		t.Skip("store does not support notifications")
	}
	appendMessages(t, subject, message(1))
	select {
	case <-notifications:
	case <-time.After(eventually):
		t.Fatal("no notification after a committed append")
	}
}

func testOldestPending(t *testing.T, subject Subject) {
	reporter, ok := subject.Store.(outbox.LagReporter)
	if !ok {
		t.Skip("store does not implement outbox.LagReporter")
	}
	ctx := context.Background()
	if _, found, err := reporter.OldestPending(ctx); err != nil || found {
		t.Fatalf("OldestPending on an empty store = %v, %v", found, err)
	}

	appendRange(t, subject, 2)
	claimed := claim(t, subject, 10, time.Minute)
	oldest, found, err := reporter.OldestPending(ctx)
	if err != nil || !found || !oldest.Equal(claimed[0].CreatedAt) {
		t.Fatalf("OldestPending = %v, %v, %v; want %v", oldest, found, err, claimed[0].CreatedAt)
	}

	if err := subject.Store.MarkPublished(ctx, ids(claimed)...); err != nil {
		t.Fatal(err)
	}
	if _, found, err := reporter.OldestPending(ctx); err != nil || found {
		t.Fatalf("OldestPending after publishing everything = %v, %v", found, err)
	}
}
