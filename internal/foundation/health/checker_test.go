package health_test

import (
	"context"
	"encoding/json"
	"errors"
	"strings"
	"sync/atomic"
	"testing"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/health"
)

// TestStartRunsChecksSynchronouslyBeforeReturning verifies that Start runs
// every check once, synchronously, so the very first Report is accurate
// immediately after Start returns, without waiting for the interval.
func TestStartRunsChecksSynchronouslyBeforeReturning(t *testing.T) {
	var ran atomic.Bool
	checker, err := health.NewChecker([]health.Check{
		{Name: "eager", Run: func(context.Context) error {
			ran.Store(true)
			return nil
		}},
	}, health.Settings{Interval: time.Hour, Timeout: time.Second, FailureThreshold: 1})
	if err != nil {
		t.Fatalf("NewChecker returned unexpected error: %v", err)
	}

	if err := checker.Start(context.Background()); err != nil {
		t.Fatalf("Start returned unexpected error: %v", err)
	}
	defer func() { _ = checker.Stop(context.Background()) }()

	if !ran.Load() {
		t.Fatal("Start did not run the check synchronously")
	}
	if !checker.Ready() {
		t.Fatal("Ready() = false after a synchronous passing run")
	}
}

// TestFailureThresholdMarksCheckFailingOnlyAfterNConsecutiveFailures
// verifies that, once a check has succeeded at least once, it is not
// marked failing again until it has accumulated the configured number of
// consecutive failures (the threshold smooths transient flapping for a
// check that has already proven healthy; it does not apply to a check
// that has never succeeded, see
// TestReadyIsFalseImmediatelyWhenADependencyIsDownAtBoot).
func TestFailureThresholdMarksCheckFailingOnlyAfterNConsecutiveFailures(t *testing.T) {
	var failures atomic.Int32
	var succeedFirst atomic.Bool
	succeedFirst.Store(true)

	checker, err := health.NewChecker([]health.Check{
		{Name: "flaky", Run: func(context.Context) error {
			if succeedFirst.Load() {
				return nil
			}
			failures.Add(1)
			return errors.New("boom")
		}},
	}, health.Settings{Interval: time.Millisecond, Timeout: time.Second, FailureThreshold: 3})
	if err != nil {
		t.Fatalf("NewChecker returned unexpected error: %v", err)
	}

	if err := checker.Start(context.Background()); err != nil {
		t.Fatalf("Start returned unexpected error: %v", err)
	}
	defer func() { _ = checker.Stop(context.Background()) }()

	if !checker.Ready() {
		t.Fatal("Ready() = false after the initial passing run, want true")
	}

	succeedFirst.Store(false)

	if !checker.Ready() {
		t.Fatal("Ready() = false immediately after the first failure following a success, want true until threshold is reached")
	}

	waitUntil(t, func() bool { return failures.Load() >= 3 })
	waitUntil(t, func() bool { return !checker.Ready() })
}

// TestOneSuccessRestoresAFailingCheck verifies that a single successful
// run immediately clears a failing check's status, regardless of how many
// consecutive failures preceded it.
func TestOneSuccessRestoresAFailingCheck(t *testing.T) {
	var failing atomic.Bool
	failing.Store(true)

	checker, err := health.NewChecker([]health.Check{
		{Name: "recovering", Run: func(context.Context) error {
			if failing.Load() {
				return errors.New("still down")
			}
			return nil
		}},
	}, health.Settings{Interval: time.Millisecond, Timeout: time.Second, FailureThreshold: 1})
	if err != nil {
		t.Fatalf("NewChecker returned unexpected error: %v", err)
	}

	if err := checker.Start(context.Background()); err != nil {
		t.Fatalf("Start returned unexpected error: %v", err)
	}
	defer func() { _ = checker.Stop(context.Background()) }()

	waitUntil(t, func() bool { return !checker.Ready() })

	failing.Store(false)

	waitUntil(t, func() bool { return checker.Ready() })

	report := checker.Report()
	status := report.Checks["recovering"]
	if status.ConsecutiveFailures != 0 {
		t.Fatalf("ConsecutiveFailures = %d, want 0 after a success", status.ConsecutiveFailures)
	}
}

// TestTimeoutCountsAsFailure verifies that a check which exceeds the
// configured per-check timeout is treated as a failed run.
func TestTimeoutCountsAsFailure(t *testing.T) {
	checker, err := health.NewChecker([]health.Check{
		{Name: "slow", Run: func(ctx context.Context) error {
			<-ctx.Done()
			return ctx.Err()
		}},
	}, health.Settings{Interval: time.Hour, Timeout: time.Millisecond, FailureThreshold: 1})
	if err != nil {
		t.Fatalf("NewChecker returned unexpected error: %v", err)
	}

	if err := checker.Start(context.Background()); err != nil {
		t.Fatalf("Start returned unexpected error: %v", err)
	}
	defer func() { _ = checker.Stop(context.Background()) }()

	if checker.Ready() {
		t.Fatal("Ready() = true for a check that timed out")
	}

	status := checker.Report().Checks["slow"]
	if status.Status != health.StatusFail {
		t.Fatalf("status = %q, want %q", status.Status, health.StatusFail)
	}
}

// TestReportShapeReflectsEveryCheck verifies that Report aggregates an
// overall pass/fail status and includes every registered check by name.
func TestReportShapeReflectsEveryCheck(t *testing.T) {
	checker, err := health.NewChecker([]health.Check{
		{Name: "ok", Run: func(context.Context) error { return nil }},
		{Name: "broken", Run: func(context.Context) error { return errors.New("nope") }},
	}, health.Settings{Interval: time.Hour, Timeout: time.Second, FailureThreshold: 1})
	if err != nil {
		t.Fatalf("NewChecker returned unexpected error: %v", err)
	}

	if err := checker.Start(context.Background()); err != nil {
		t.Fatalf("Start returned unexpected error: %v", err)
	}
	defer func() { _ = checker.Stop(context.Background()) }()

	report := checker.Report()
	if report.Status != health.StatusFail {
		t.Fatalf("overall status = %q, want %q", report.Status, health.StatusFail)
	}
	if len(report.Checks) != 2 {
		t.Fatalf("len(Checks) = %d, want 2", len(report.Checks))
	}
	if report.Checks["ok"].Status != health.StatusPass {
		t.Fatalf(`Checks["ok"].Status = %q, want %q`, report.Checks["ok"].Status, health.StatusPass)
	}
	if report.Checks["broken"].Output != "nope" {
		t.Fatalf(`Checks["broken"].Output = %q, want %q`, report.Checks["broken"].Output, "nope")
	}
}

// TestStopWaitsForTheBackgroundGoroutine verifies that Stop does not
// return until the background loop goroutine has exited.
func TestStopWaitsForTheBackgroundGoroutine(t *testing.T) {
	var runs atomic.Int32
	checker, err := health.NewChecker([]health.Check{
		{Name: "counting", Run: func(context.Context) error {
			runs.Add(1)
			return nil
		}},
	}, health.Settings{Interval: time.Millisecond, Timeout: time.Second, FailureThreshold: 1})
	if err != nil {
		t.Fatalf("NewChecker returned unexpected error: %v", err)
	}

	if err := checker.Start(context.Background()); err != nil {
		t.Fatalf("Start returned unexpected error: %v", err)
	}

	waitUntil(t, func() bool { return runs.Load() >= 2 })

	if err := checker.Stop(context.Background()); err != nil {
		t.Fatalf("Stop returned unexpected error: %v", err)
	}

	observedAfterStop := runs.Load()
	time.Sleep(10 * time.Millisecond)
	if runs.Load() != observedAfterStop {
		t.Fatal("check kept running after Stop returned")
	}
}

// TestStopReturnsWhenContextIsDone verifies that Stop respects context
// cancellation instead of blocking forever, when the background loop's
// in-flight run does not observe the loop's own cancellation (it ignores
// ctx entirely and only unblocks on blockForever).
func TestStopReturnsWhenContextIsDone(t *testing.T) {
	var calls atomic.Int32
	blockForever := make(chan struct{})
	checker, err := health.NewChecker([]health.Check{
		{Name: "blocking", Run: func(context.Context) error {
			if calls.Add(1) == 1 {
				// First call: the synchronous run inside Start. Return
				// immediately so Start returns and the loop starts.
				return nil
			}
			<-blockForever
			return nil
		}},
	}, health.Settings{Interval: time.Millisecond, Timeout: time.Hour, FailureThreshold: 1})
	if err != nil {
		t.Fatalf("NewChecker returned unexpected error: %v", err)
	}

	if startErr := checker.Start(context.Background()); startErr != nil {
		t.Fatalf("Start returned unexpected error: %v", startErr)
	}

	waitUntil(t, func() bool { return calls.Load() >= 2 })

	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Millisecond)
	defer cancel()

	err = checker.Stop(ctx)
	if !errors.Is(err, context.DeadlineExceeded) {
		t.Fatalf("Stop error = %v, want context.DeadlineExceeded", err)
	}
	close(blockForever)
}

// TestReadyIsFalseImmediatelyWhenADependencyIsDownAtBoot verifies that a
// check failing on its very first run marks the checker not-ready right
// away, instead of requiring FailureThreshold consecutive failures before
// the first ever success. The threshold only smooths transient flapping
// once a check has proven healthy at least once.
func TestReadyIsFalseImmediatelyWhenADependencyIsDownAtBoot(t *testing.T) {
	checker, err := health.NewChecker([]health.Check{
		{Name: "down-at-boot", Run: func(context.Context) error { return errors.New("boom") }},
	}, health.Settings{Interval: time.Hour, Timeout: time.Second, FailureThreshold: 3})
	if err != nil {
		t.Fatalf("NewChecker returned unexpected error: %v", err)
	}

	if err := checker.Start(context.Background()); err != nil {
		t.Fatalf("Start returned unexpected error: %v", err)
	}
	defer func() { _ = checker.Stop(context.Background()) }()

	if checker.Ready() {
		t.Fatal("Ready() = true immediately after Start with a dependency down at boot, want false")
	}
}

// TestASlowCheckDoesNotDelayOtherChecksWithinACycle verifies that every
// registered check runs concurrently within one cycle: a slow check that
// only returns once its own (long) per-check timeout elapses does not
// delay a fast check's update, which must land promptly instead of
// waiting for the slow one to finish first, as sequential execution
// would force.
func TestASlowCheckDoesNotDelayOtherChecksWithinACycle(t *testing.T) {
	var fastRuns atomic.Int32
	checker, err := health.NewChecker([]health.Check{
		{Name: "slow", Run: func(ctx context.Context) error {
			<-ctx.Done()
			return ctx.Err()
		}},
		{Name: "fast", Run: func(context.Context) error {
			fastRuns.Add(1)
			return nil
		}},
	}, health.Settings{Interval: time.Hour, Timeout: 500 * time.Millisecond, FailureThreshold: 1})
	if err != nil {
		t.Fatalf("NewChecker returned unexpected error: %v", err)
	}

	go func() { _ = checker.Start(context.Background()) }()
	defer func() { _ = checker.Stop(context.Background()) }()

	waitUntilOrFail(t, 100*time.Millisecond, func() bool { return fastRuns.Load() >= 1 })
}

// TestStartIsNotIdempotent verifies that calling Start a second time
// returns an error instead of racing the first call's background loop
// setup (which writes checker.cancel and checker.done unguarded).
func TestStartIsNotIdempotent(t *testing.T) {
	checker, err := health.NewChecker(nil, health.Settings{Interval: time.Hour, Timeout: time.Second, FailureThreshold: 1})
	if err != nil {
		t.Fatalf("NewChecker returned unexpected error: %v", err)
	}

	if err := checker.Start(context.Background()); err != nil {
		t.Fatalf("first Start returned unexpected error: %v", err)
	}
	defer func() { _ = checker.Stop(context.Background()) }()

	if err := checker.Start(context.Background()); err == nil {
		t.Fatal("second Start returned nil error, want an error")
	}
}

// TestStartAndStopAreSafeForConcurrentUse verifies, under the race
// detector, that Start and Stop do not race on the checker's internal
// cancel/done bookkeeping when called from different goroutines.
func TestStartAndStopAreSafeForConcurrentUse(t *testing.T) {
	checker, err := health.NewChecker(nil, health.Settings{Interval: time.Hour, Timeout: time.Second, FailureThreshold: 1})
	if err != nil {
		t.Fatalf("NewChecker returned unexpected error: %v", err)
	}

	started := make(chan struct{})
	go func() {
		_ = checker.Start(context.Background())
		close(started)
	}()
	<-started

	if err := checker.Stop(context.Background()); err != nil {
		t.Fatalf("Stop returned unexpected error: %v", err)
	}
}

// TestNewCheckerRejectsDuplicateCheckNames verifies that two checks
// registered under the same name are rejected instead of silently
// letting the second overwrite the first's state.
func TestNewCheckerRejectsDuplicateCheckNames(t *testing.T) {
	_, err := health.NewChecker([]health.Check{
		{Name: "same", Run: func(context.Context) error { return nil }},
		{Name: "same", Run: func(context.Context) error { return nil }},
	}, health.Settings{})

	if err == nil {
		t.Fatal("NewChecker returned nil error for duplicate check names")
	}
}

// TestNewCheckerRejectsInvalidSettings verifies that NewChecker validates
// settings instead of silently ignoring an invalid explicit value.
func TestNewCheckerRejectsInvalidSettings(t *testing.T) {
	_, err := health.NewChecker(nil, health.Settings{Interval: -1})

	if err == nil {
		t.Fatal("NewChecker returned nil error for a negative Interval")
	}
}

// waitUntil polls condition until it is true or a short deadline elapses,
// failing the test if the deadline is reached first.
func waitUntil(t *testing.T, condition func() bool) {
	t.Helper()
	waitUntilOrFail(t, 2*time.Second, condition)
}

// waitUntilOrFail polls condition until it is true or timeout elapses,
// failing the test if the deadline is reached first.
func waitUntilOrFail(t *testing.T, timeout time.Duration, condition func() bool) {
	t.Helper()
	deadline := time.Now().Add(timeout)
	for time.Now().Before(deadline) {
		if condition() {
			return
		}
		time.Sleep(time.Millisecond)
	}
	t.Fatal("condition was never satisfied")
}

func TestReportSerializesDurationInMilliseconds(t *testing.T) {
	status := health.CheckStatus{DurationMilliseconds: 1500}

	encoded, err := json.Marshal(status)
	if err != nil {
		t.Fatalf("json.Marshal: %v", err)
	}
	if !strings.Contains(string(encoded), `"durationMilliseconds":1500`) {
		t.Errorf("encoded check status = %s, want durationMilliseconds 1500", encoded)
	}
}
