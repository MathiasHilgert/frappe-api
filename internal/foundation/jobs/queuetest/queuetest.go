// Package queuetest is the contract suite every jobs backend adapter must
// pass: it proves execution, argument and metadata round trips, delays,
// uniqueness, retries, cancellation and snoozing against a real backend.
//
//	func TestIntegrationContract(t *testing.T) {
//		queuetest.Run(t, func(t *testing.T, catalog *jobs.Catalog) jobs.Enqueuer {
//			return startedBackend(t, catalog)
//		})
//	}
package queuetest

import (
	"context"
	"errors"
	"sync"
	"testing"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/jobs"
)

// eventually bounds every asynchronous expectation of the suite.
const eventually = 20 * time.Second

// Factory starts a backend working every handler registered on catalog
// (the suite registers them before calling it) and returns its enqueuer.
// The backend must be stopped when t ends.
type Factory func(t *testing.T, catalog *jobs.Catalog) jobs.Enqueuer

// Arguments is the payload of every suite job.
type Arguments struct {
	Key string `json:"key"`
}

// recorder collects the jobs a handler received.
type recorder struct {
	received []jobs.Job[Arguments]
	mutex    sync.Mutex
}

func (recorder *recorder) add(job jobs.Job[Arguments]) int {
	recorder.mutex.Lock()
	defer recorder.mutex.Unlock()
	recorder.received = append(recorder.received, job)
	return len(recorder.received)
}

func (recorder *recorder) all() []jobs.Job[Arguments] {
	recorder.mutex.Lock()
	defer recorder.mutex.Unlock()
	return append([]jobs.Job[Arguments](nil), recorder.received...)
}

func (recorder *recorder) waitFor(t *testing.T, count int) []jobs.Job[Arguments] {
	t.Helper()
	deadline := time.Now().Add(eventually)
	for time.Now().Before(deadline) {
		if received := recorder.all(); len(received) >= count {
			return received
		}
		time.Sleep(50 * time.Millisecond)
	}
	t.Fatalf("queuetest: got %d executions, want %d", len(recorder.all()), count)
	return nil
}

type tenantKey struct{}

// Run executes the contract suite against the backend built by factory.
func Run(t *testing.T, factory Factory) {
	t.Helper()
	t.Run("ExecutesWithArgumentsTenantAndAttempt", func(t *testing.T) { executesWithArgumentsTenantAndAttempt(t, factory) })
	t.Run("DelaysWithAfter", func(t *testing.T) { delaysWithAfter(t, factory) })
	t.Run("UniqueSkipsDuplicates", func(t *testing.T) { uniqueSkipsDuplicates(t, factory) })
	t.Run("RetriesFailures", func(t *testing.T) { retriesFailures(t, factory) })
	t.Run("CancelStopsRetries", func(t *testing.T) { cancelStopsRetries(t, factory) })
	t.Run("SnoozeRunsAgainLater", func(t *testing.T) { snoozeRunsAgainLater(t, factory) })
}

func executesWithArgumentsTenantAndAttempt(t *testing.T, factory Factory) {
	catalog := jobs.NewCatalog()
	catalog.UseTenancy(jobs.Tenancy{Resolve: func(ctx context.Context) (string, bool) {
		tenant, ok := ctx.Value(tenantKey{}).(string)
		return tenant, ok
	}})
	received := &recorder{}
	definition := jobs.Define[Arguments](catalog.For("contract"), "execute")
	jobs.Handle(definition, func(_ context.Context, job jobs.Job[Arguments]) error {
		received.add(job)
		return nil
	})
	enqueue(context.WithValue(context.Background(), tenantKey{}, "tenant-a"), t, factory, catalog, definition, Arguments{Key: "k-1"})

	job := received.waitFor(t, 1)[0]
	if job.Args.Key != "k-1" || job.Tenant != "tenant-a" || job.Attempt != 1 || job.ID == 0 || job.Name != "contract.execute" {
		t.Fatalf("job = %+v", job)
	}
}

func delaysWithAfter(t *testing.T, factory Factory) {
	catalog := jobs.NewCatalog()
	received := &recorder{}
	definition := jobs.Define[Arguments](catalog.For("contract"), "delay")
	jobs.Handle(definition, func(_ context.Context, job jobs.Job[Arguments]) error {
		received.add(job)
		return nil
	})
	enqueuedAt := time.Now()
	enqueue(context.Background(), t, factory, catalog, definition, Arguments{Key: "later"}, jobs.After(2*time.Second))
	received.waitFor(t, 1)
	if elapsed := time.Since(enqueuedAt); elapsed < 2*time.Second {
		t.Fatalf("delayed job ran after %s, want at least 2s", elapsed)
	}
}

func uniqueSkipsDuplicates(t *testing.T, factory Factory) {
	catalog := jobs.NewCatalog()
	definition := jobs.Define[Arguments](catalog.For("contract"), "unique")
	jobs.Handle(definition, func(context.Context, jobs.Job[Arguments]) error { return nil })
	enqueuer := factory(t, catalog)
	ctx := jobs.ContextWithEnqueuer(context.Background(), enqueuer)
	first, err := definition.Enqueue(ctx, Arguments{Key: "same"}, jobs.After(time.Hour), jobs.Unique(0))
	if err != nil {
		t.Fatal(err)
	}
	second, err := definition.Enqueue(ctx, Arguments{Key: "same"}, jobs.After(time.Hour), jobs.Unique(0))
	if err != nil {
		t.Fatal(err)
	}
	if first.Duplicate || !second.Duplicate || second.ID != first.ID {
		t.Fatalf("first = %+v, second = %+v", first, second)
	}
	other, err := definition.Enqueue(ctx, Arguments{Key: "other"}, jobs.After(time.Hour), jobs.Unique(0))
	if err != nil || other.Duplicate {
		t.Fatalf("different arguments must not be duplicates: %+v %v", other, err)
	}
}

func retriesFailures(t *testing.T, factory Factory) {
	catalog := jobs.NewCatalog()
	received := &recorder{}
	definition := jobs.Define[Arguments](catalog.For("contract"), "retry", jobs.WithMaxAttempts(3))
	jobs.Handle(definition, func(_ context.Context, job jobs.Job[Arguments]) error {
		if received.add(job) == 1 {
			return errors.New("temporary")
		}
		return nil
	})
	enqueue(context.Background(), t, factory, catalog, definition, Arguments{Key: "retry"})
	attempts := received.waitFor(t, 2)
	if attempts[0].Attempt != 1 || attempts[1].Attempt != 2 || attempts[1].MaxAttempts != 3 {
		t.Fatalf("attempts = %+v", attempts)
	}
}

func cancelStopsRetries(t *testing.T, factory Factory) {
	catalog := jobs.NewCatalog()
	received := &recorder{}
	definition := jobs.Define[Arguments](catalog.For("contract"), "cancel", jobs.WithMaxAttempts(5))
	jobs.Handle(definition, func(_ context.Context, job jobs.Job[Arguments]) error {
		received.add(job)
		return jobs.Cancel(errors.New("gone"))
	})
	enqueue(context.Background(), t, factory, catalog, definition, Arguments{Key: "cancel"})
	received.waitFor(t, 1)
	time.Sleep(3 * time.Second)
	if count := len(received.all()); count != 1 {
		t.Fatalf("cancelled job ran %d times, want 1", count)
	}
}

func snoozeRunsAgainLater(t *testing.T, factory Factory) {
	catalog := jobs.NewCatalog()
	received := &recorder{}
	definition := jobs.Define[Arguments](catalog.For("contract"), "snooze", jobs.WithMaxAttempts(1))
	jobs.Handle(definition, func(_ context.Context, job jobs.Job[Arguments]) error {
		if received.add(job) == 1 {
			return jobs.Snooze(time.Second)
		}
		return nil
	})
	enqueue(context.Background(), t, factory, catalog, definition, Arguments{Key: "snooze"})
	executions := received.waitFor(t, 2)
	if executions[0].ID != executions[1].ID {
		t.Fatalf("snoozed job must run again as the same job: %+v", executions)
	}
}

func enqueue(ctx context.Context, t *testing.T, factory Factory, catalog *jobs.Catalog, definition *jobs.Definition[Arguments], args Arguments, options ...jobs.EnqueueOption) {
	t.Helper()
	enqueuer := factory(t, catalog)
	if _, err := definition.Enqueue(jobs.ContextWithEnqueuer(ctx, enqueuer), args, options...); err != nil {
		t.Fatalf("queuetest: Enqueue() = %v", err)
	}
}
