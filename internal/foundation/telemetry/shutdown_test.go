package telemetry

import (
	"context"
	"errors"
	"testing"
	"time"
)

// delayedShutdowner is a fake shutdowner that blocks for delay before
// returning err, so tests can prove Down shuts every provider down
// concurrently instead of one after another: shutting down three fakes
// with the same delay takes roughly one delay, not three, only if they
// run concurrently.
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

// TestDownShutsDownProvidersConcurrently proves that Down shuts the
// tracer, meter and logger providers down concurrently: with three fakes
// that each take delay to shut down, Down must return in about one delay,
// not the sum of all three, so a hung export on one signal never starves
// the others.
func TestDownShutsDownProvidersConcurrently(t *testing.T) {
	const delay = 100 * time.Millisecond

	value := SDK{
		tracerProvider: delayedShutdowner{delay: delay},
		meterProvider:  delayedShutdowner{delay: delay},
		loggerProvider: delayedShutdowner{delay: delay},
	}

	start := time.Now()
	if err := Down(context.Background(), value); err != nil {
		t.Fatalf("Down returned unexpected error: %v", err)
	}
	elapsed := time.Since(start)

	// A generous upper bound: sequential shutdown of three delay-long
	// fakes would take about 3*delay (300ms); concurrent shutdown takes
	// about one delay. 2*delay leaves headroom for scheduling jitter
	// while still failing if shutdown were sequential.
	if elapsed >= 2*delay {
		t.Fatalf("Down took %v, want well under %v (providers shut down sequentially, not concurrently)", elapsed, 2*delay)
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
