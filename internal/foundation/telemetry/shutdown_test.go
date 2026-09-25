package telemetry

import (
	"context"
	"errors"
	"fmt"
	"sync"
	"testing"
	"time"
)

// delayedShutdowner is a fake shutdowner that blocks for delay before
// returning err.
type delayedShutdowner struct {
	err   error
	delay time.Duration
}

func (fake delayedShutdowner) Shutdown(ctx context.Context) error {
	select {
	case <-time.After(fake.delay):
		return fake.err
	case <-ctx.Done():
		return ctx.Err()
	}
}

// rendezvousBarrier lets a fixed number of parties prove they were all
// "in flight" at the same time: each party calls arrive, which blocks
// until every party has called it (or timeout elapses first). This is a
// deterministic way to prove concurrency, independent of how fast or slow
// the machine running the test is: if the parties run one after another
// instead of concurrently, the first one blocks in arrive until timeout,
// however generous, and reports that failure; if they run concurrently,
// arrive returns almost immediately for all of them, regardless of
// machine speed.
type rendezvousBarrier struct {
	done    chan struct{}
	mutex   sync.Mutex
	once    sync.Once
	parties int
	arrived int
}

func newRendezvousBarrier(parties int) *rendezvousBarrier {
	return &rendezvousBarrier{parties: parties, done: make(chan struct{})}
}

// arrive blocks until every party has called arrive, or timeout elapses
// first, in which case it returns an error naming how many parties had
// arrived.
func (barrier *rendezvousBarrier) arrive(timeout time.Duration) error {
	barrier.mutex.Lock()
	barrier.arrived++
	reached := barrier.arrived == barrier.parties
	arrivedSoFar := barrier.arrived
	barrier.mutex.Unlock()

	if reached {
		barrier.once.Do(func() { close(barrier.done) })
	}

	select {
	case <-barrier.done:
		return nil
	case <-time.After(timeout):
		return fmt.Errorf("rendezvousBarrier: timed out, not every party arrived concurrently (arrived %d of %d)", arrivedSoFar, barrier.parties)
	}
}

// barrierShutdowner is a fake shutdowner whose Shutdown blocks on a shared
// rendezvousBarrier, so a test can deterministically prove several
// barrierShutdowners were shut down concurrently: Shutdown only returns
// once every one of them has entered Shutdown, however long that takes on
// a slow CI machine, and only fails if one of them is never joined by the
// others within the generous timeout (which only happens if Down shuts
// them down one after another instead of concurrently).
type barrierShutdowner struct {
	barrier *rendezvousBarrier
	timeout time.Duration
}

func (fake barrierShutdowner) Shutdown(context.Context) error {
	return fake.barrier.arrive(fake.timeout)
}

// TestDownShutsDownProvidersConcurrently proves that Down shuts the
// tracer, meter and logger providers down concurrently, not one after
// another, so a hung export on one signal never starves the others. It
// uses a rendezvous barrier rather than a wall-clock bound, so it fails
// only when the three providers are never all "in flight" together,
// regardless of how fast or slow the machine running the test is.
func TestDownShutsDownProvidersConcurrently(t *testing.T) {
	// Generous: this only needs to be reached when Down does shut the
	// providers down concurrently, which takes microseconds, not seconds.
	// It is not a sequential-case budget; a sequential Down would leave
	// the first fake waiting the full timeout with only itself arrived.
	const timeout = 5 * time.Second

	barrier := newRendezvousBarrier(3)
	value := SDK{
		tracerProvider: barrierShutdowner{barrier: barrier, timeout: timeout},
		meterProvider:  barrierShutdowner{barrier: barrier, timeout: timeout},
		loggerProvider: barrierShutdowner{barrier: barrier, timeout: timeout},
	}

	if err := Down(context.Background(), value); err != nil {
		t.Fatalf("Down returned unexpected error: %v (providers were not all shut down concurrently)", err)
	}
}

// TestDownJoinsErrorsFromEveryProvider proves that a failure shutting
// down one provider does not prevent Down from also shutting down (and
// reporting the failure of) the others.
func TestDownJoinsErrorsFromEveryProvider(t *testing.T) {
	tracerErr := errors.New("tracer shutdown failed")
	loggerErr := errors.New("logger shutdown failed")

	value := SDK{
		tracerProvider: delayedShutdowner{err: tracerErr},
		meterProvider:  delayedShutdowner{},
		loggerProvider: delayedShutdowner{err: loggerErr},
	}

	err := Down(context.Background(), value)
	if err == nil {
		t.Fatal("Down returned nil error, want the joined shutdown failures")
	}
	if !errors.Is(err, tracerErr) {
		t.Fatalf("Down error = %v, want it to wrap %v", err, tracerErr)
	}
	if !errors.Is(err, loggerErr) {
		t.Fatalf("Down error = %v, want it to wrap %v", err, loggerErr)
	}
}
