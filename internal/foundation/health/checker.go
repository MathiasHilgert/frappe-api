package health

import (
	"context"
	"errors"
	"fmt"
	"log/slog"
	"sync"
	"time"
)

// state is one check's concurrency-safe, mutable status.
type state struct {
	lastCheckedAt       time.Time
	lastError           error
	lastDuration        time.Duration
	consecutiveFailures int
	failing             bool
	// succeededOnce is true once this check has passed at least one run.
	// Until then, any single failure marks the check failing immediately:
	// FailureThreshold only smooths transient flapping for a check that
	// has already proven healthy, it must not let a dependency that is
	// down from boot report ready while failures below the threshold
	// accumulate.
	succeededOnce bool
}

// snapshot copies state under the caller-held lock into an immutable
// CheckStatus. Output is a generic, safe-to-expose message, never the raw
// error text: see CheckStatus.Output's own doc comment for why.
func (checkState state) snapshot() CheckStatus {
	status := CheckStatus{
		Time:          checkState.lastCheckedAt,
		Status:        StatusPass,
		ObservedUnit:  "ms",
		ObservedValue: float64(checkState.lastDuration) / float64(time.Millisecond),
	}
	if checkState.failing {
		status.Status = StatusFail
	}
	if checkState.lastError != nil {
		status.Output = genericOutput(checkState.lastError)
	}
	return status
}

// genericOutput turns err into a fixed, safe-to-expose message instead of
// its raw text, which may contain connection strings, hostnames,
// credentials or other internal detail. A context deadline is reported
// distinctly as a timeout, since that distinction is useful externally
// without leaking anything.
func genericOutput(err error) string {
	if errors.Is(err, context.DeadlineExceeded) {
		return "check failed: timeout"
	}
	return "check failed"
}

// Checker runs a fixed set of registered checks periodically in the
// background and exposes their aggregate result as a Report. It is safe
// for concurrent use: Report and Ready may be called from an HTTP
// handler's goroutine while the background loop updates state.
type Checker struct {
	states   map[string]*state
	cancel   context.CancelFunc
	done     chan struct{}
	checks   []Check
	settings Settings
	mutex    sync.Mutex
	// lifecycleMutex guards cancel, done and started, which are written
	// once by Start and read by Stop; it is separate from mutex (which
	// guards per-check state) so Stop is never blocked behind an
	// in-flight check run.
	lifecycleMutex sync.Mutex
	started        bool
}

// NewChecker creates a Checker for the given checks, applying settings with
// package defaults filled in for any zero-valued field. It returns an
// error if settings is invalid (see Settings.Validate) or if two checks
// share the same name, which would otherwise let the second silently
// overwrite the first's state.
func NewChecker(checks []Check, settings Settings) (*Checker, error) {
	if err := settings.Validate(); err != nil {
		return nil, err
	}

	states := make(map[string]*state, len(checks))
	for _, check := range checks {
		if _, exists := states[check.Name]; exists {
			return nil, fmt.Errorf("health: duplicate check name %q", check.Name)
		}
		states[check.Name] = &state{}
	}

	return &Checker{
		settings: settings.withDefaults(),
		checks:   checks,
		states:   states,
	}, nil
}

// Start runs every registered check once, synchronously, so the very
// first Report is accurate immediately, and then launches a background
// goroutine that re-runs every check on the configured interval. Start
// itself does not block on the background loop, and the loop keeps
// running independently of ctx: cancel it with Stop, not by canceling
// ctx.
func (checker *Checker) Start(ctx context.Context) error {
	checker.lifecycleMutex.Lock()
	if checker.started {
		checker.lifecycleMutex.Unlock()
		return errors.New("health: Start called more than once")
	}
	checker.started = true

	loopCtx, cancel := context.WithCancel(context.Background())
	checker.cancel = cancel
	checker.done = make(chan struct{})
	checker.lifecycleMutex.Unlock()

	checker.runAll(ctx)

	go checker.loop(loopCtx)

	return nil
}

// Stop cancels the background loop and waits for it to finish before
// returning, or until ctx is done, whichever comes first. Calling Stop
// before Start, or more than once, is a no-op.
func (checker *Checker) Stop(ctx context.Context) error {
	checker.lifecycleMutex.Lock()
	cancel := checker.cancel
	done := checker.done
	checker.lifecycleMutex.Unlock()

	if cancel == nil {
		return nil
	}
	cancel()

	select {
	case <-done:
	case <-ctx.Done():
		return ctx.Err()
	}
	return nil
}

// loop re-runs every check on the configured interval until ctx is done.
func (checker *Checker) loop(ctx context.Context) {
	defer close(checker.done)

	ticker := time.NewTicker(checker.settings.Interval)
	defer ticker.Stop()

	for {
		select {
		case <-ctx.Done():
			return
		case <-ticker.C:
			checker.runAll(ctx)
		}
	}
}

// runAll runs every registered check once, concurrently, each bounded by
// its own per-check timeout, so one check that hangs (in particular one
// that ignores the context it is given, despite Check.Run's contract)
// cannot stall the others' state from updating within the same cycle.
func (checker *Checker) runAll(ctx context.Context) {
	var waitGroup sync.WaitGroup
	for _, check := range checker.checks {
		waitGroup.Add(1)
		go func(check Check) {
			defer waitGroup.Done()
			checker.run(ctx, check)
		}(check)
	}
	waitGroup.Wait()
}

// run executes one check under a timeout-bounded context and updates its
// state and metrics.
func (checker *Checker) run(ctx context.Context, check Check) {
	runCtx, cancel := context.WithTimeout(ctx, checker.settings.Timeout)
	defer cancel()

	start := time.Now()
	err := check.Run(runCtx)
	duration := time.Since(start)

	checker.mutex.Lock()
	checkState := checker.states[check.Name]
	wasFailing := checkState.failing
	checkState.lastCheckedAt = start
	checkState.lastDuration = duration
	if err != nil {
		checkState.lastError = err
		checkState.consecutiveFailures++
		if !checkState.succeededOnce || checkState.consecutiveFailures >= checker.settings.FailureThreshold {
			checkState.failing = true
		}
	} else {
		checkState.lastError = nil
		checkState.consecutiveFailures = 0
		checkState.failing = false
		checkState.succeededOnce = true
	}
	passing := !checkState.failing
	transitioned := checkState.failing != wasFailing
	checker.mutex.Unlock()

	// The detailed error is only ever logged on a pass/fail transition,
	// never on every run, so a check that stays failing for a long time
	// does not spam the log; CheckStatus.Output stays a generic message
	// regardless (see genericOutput).
	if transitioned {
		checker.logTransition(check.Name, passing, err)
	}

	recordCheck(ctx, check.Name, duration, passing)
}

// logTransition records a check's pass/fail transition through the
// resolved logger, including the detailed error when the check just
// started failing.
func (checker *Checker) logTransition(name string, passing bool, err error) {
	logger := checker.logger()
	if passing {
		logger.Info("health check recovered", slog.String("check", name))
		return
	}
	logger.Error("health check failing", slog.String("check", name), slog.Any("error", err))
}

// logger returns the configured logger, resolving slog.Default() lazily
// each time a transition logs, rather than capturing it once, when none
// was set through Settings.Logger.
func (checker *Checker) logger() *slog.Logger {
	if checker.settings.Logger != nil {
		return checker.settings.Logger
	}
	return slog.Default()
}

// Ready reports whether every registered check is currently passing. A
// Checker with no registered checks is always ready. It is equivalent to
// the second return value of Snapshot, provided as its own method for
// callers that only need the boolean; a caller that needs both a report
// and the readiness it implies (for example to build one consistent HTTP
// response) must use Snapshot instead of calling Ready and Report
// separately, since a state update between those two separate calls
// could otherwise make them disagree.
func (checker *Checker) Ready() bool {
	_, ready := checker.Snapshot()
	return ready
}

// Report builds the current aggregate Report from every registered
// check's state. See Snapshot's doc comment for why a caller that also
// needs the readiness this report implies should call Snapshot instead.
func (checker *Checker) Report() Report {
	report, _ := checker.Snapshot()
	return report
}

// Snapshot builds the current aggregate Report and its implied readiness
// together, under a single lock, so the two can never disagree the way
// two separate Ready and Report calls could if a state update landed
// between them. ready is true if and only if report.Status is
// StatusPass.
func (checker *Checker) Snapshot() (Report, bool) {
	checker.mutex.Lock()
	defer checker.mutex.Unlock()

	report := Report{
		Status: StatusPass,
		Checks: make(map[string][]CheckStatus, len(checker.states)),
	}

	for _, check := range checker.checks {
		checkState := checker.states[check.Name]
		status := checkState.snapshot()
		report.Checks[check.Name+":"+responseTimeMeasurement] = []CheckStatus{status}
		if status.Status == StatusFail {
			report.Status = StatusFail
		}
	}

	return report, report.Status == StatusPass
}
