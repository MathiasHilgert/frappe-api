// Package jobstest provides test doubles for the jobs package.
//
// Use cases enqueue through jobs definitions built on a catalog module; in
// unit tests build them on an isolated catalog whose enqueuer is a
// Recorder, so parallel tests never share state:
//
//	catalog, recorder := jobstest.NewCatalog()
//	menuJobs := menu.DefineJobs(catalog.Module("menu"))
//	err := useCase.Execute(ctx, input)
//	enqueued := jobstest.Enqueued(t, recorder, menuJobs.RebuildIndex)
//
// Handlers are tested through Run, which executes the registered handler
// synchronously, with the same decoding, tenant restoration and telemetry
// as production:
//
//	err := jobstest.Run(t, menuJobs.RebuildIndex, menu.RebuildIndexArgs{MenuID: id})
package jobstest

import (
	"context"
	"encoding/json"
	"sync"
	"testing"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/jobs"
)

// Recorder is an in-memory jobs.Enqueuer capturing every request. It is
// safe for concurrent use.
type Recorder struct {
	failure  error
	requests []jobs.Request
	mutex    sync.Mutex
}

// NewRecorder returns an empty Recorder.
func NewRecorder() *Recorder {
	return &Recorder{}
}

// NewCatalog returns an isolated catalog whose enqueues go to the returned
// Recorder. Tests build a module's definitions on catalog.Module("<module>")
// exactly as the composition root does.
func NewCatalog() (*jobs.Catalog, *Recorder) {
	catalog := jobs.NewCatalog()
	recorder := NewRecorder()
	// Handed over as the jobs.Enqueuer port, never the concrete type: the
	// architecture deep scan treats injecting a concrete type as the
	// receiver depending on it.
	var enqueuer jobs.Enqueuer = recorder
	catalog.Use(enqueuer)
	return catalog, recorder
}

// Enqueue implements jobs.Enqueuer.
func (recorder *Recorder) Enqueue(_ context.Context, request jobs.Request) (jobs.Receipt, error) {
	recorder.mutex.Lock()
	defer recorder.mutex.Unlock()
	if recorder.failure != nil {
		return jobs.Receipt{}, recorder.failure
	}
	recorder.requests = append(recorder.requests, request)
	return jobs.Receipt{ID: int64(len(recorder.requests))}, nil
}

// Fail makes every following Enqueue return err without capturing the job,
// to exercise failure paths. Fail(nil) restores normal behavior.
func (recorder *Recorder) Fail(err error) {
	recorder.mutex.Lock()
	defer recorder.mutex.Unlock()
	recorder.failure = err
}

// All returns every captured request in enqueue order.
func (recorder *Recorder) All() []jobs.Request {
	recorder.mutex.Lock()
	defer recorder.mutex.Unlock()
	return append([]jobs.Request(nil), recorder.requests...)
}

// Reset forgets every captured request.
func (recorder *Recorder) Reset() {
	recorder.mutex.Lock()
	defer recorder.mutex.Unlock()
	recorder.requests = nil
}

// EnqueuedJob is one captured request with its decoded arguments.
type EnqueuedJob[Args any] struct {
	Args    Args
	Request jobs.Request
}

// Enqueued returns, in enqueue order, the captured jobs of definition with
// their arguments decoded, failing the test when one cannot be decoded.
func Enqueued[Args any](t testing.TB, recorder *Recorder, definition *jobs.Definition[Args]) []EnqueuedJob[Args] {
	t.Helper()
	var matching []EnqueuedJob[Args]
	for _, request := range recorder.All() {
		if request.Name != definition.Name() {
			continue
		}
		var args Args
		if err := json.Unmarshal(request.Arguments, &args); err != nil {
			t.Fatalf("jobstest: decode arguments of %s: %v", request.Name, err)
		}
		matching = append(matching, EnqueuedJob[Args]{Args: args, Request: request})
	}
	return matching
}

// RunOption customizes the delivery Run hands to the handler.
type RunOption func(*jobs.Delivery)

// Tenant runs the job as enqueued by tenant.
func Tenant(tenant string) RunOption {
	return func(delivery *jobs.Delivery) { delivery.Metadata[jobs.MetadataTenant] = tenant }
}

// Attempt runs the job as its attempt-th attempt.
func Attempt(attempt int) RunOption {
	return func(delivery *jobs.Delivery) { delivery.Attempt = attempt }
}

// MaxAttempts runs the job with maximum attempts.
func MaxAttempts(maximum int) RunOption {
	return func(delivery *jobs.Delivery) { delivery.MaxAttempts = maximum }
}

// Run executes the handler registered for definition once, synchronously,
// with args, and returns its error. It fails the test when no handler is
// registered or args cannot be encoded.
func Run[Args any](t testing.TB, definition *jobs.Definition[Args], args Args, options ...RunOption) error {
	t.Helper()
	handler, registered := definition.Handler()
	if !registered {
		t.Fatalf("jobstest: %s has no handler; call its module's jobs.Handle first", definition.Name())
	}
	arguments, err := json.Marshal(args)
	if err != nil {
		t.Fatalf("jobstest: encode arguments of %s: %v", definition.Name(), err)
	}
	now := time.Now()
	delivery := jobs.Delivery{
		EnqueuedAt:  now,
		ScheduledAt: now,
		Metadata:    map[string]string{},
		Name:        definition.Name(),
		Queue:       definition.Queue(),
		Arguments:   arguments,
		ID:          1,
		Attempt:     1,
		MaxAttempts: 1,
	}
	for _, option := range options {
		option(&delivery)
	}
	return handler(context.Background(), delivery)
}
