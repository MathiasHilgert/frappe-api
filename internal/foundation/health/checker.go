package health

import (
	"context"
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
}

// snapshot copies state under the caller-held lock into an immutable
// CheckStatus.
func (checkState state) snapshot() CheckStatus {
	status := CheckStatus{
		LastCheckedAt:       checkState.lastCheckedAt,
		Status:              StatusPass,
		Duration:            checkState.lastDuration,
		ConsecutiveFailures: checkState.consecutiveFailures,
	}
	if checkState.failing {
		status.Status = StatusFail
	}
	if checkState.lastError != nil {
		status.Output = checkState.lastError.Error()
	}
	return status
}

// Checker runs a fixed set of registered checks periodically in the
// background and exposes their aggregate result as a Report. It is safe
// for concurrent use: Report and Ready may be called from an HTTP
// handler's goroutine while the background loop updates state.
type Checker struct {
	states map[string]*state
	cancel context.CancelFunc
	done   chan struct{}
	checks []Check
	config Config
	mutex  sync.Mutex
}

// NewChecker creates a Checker for the given checks, applying config with
// package defaults filled in for any zero-valued field.
func NewChecker(checks []Check, config Config) *Checker {
	states := make(map[string]*state, len(checks))
	for _, check := range checks {
		states[check.Name] = &state{}
	}

	return &Checker{
		config: config.withDefaults(),
		checks: checks,
		states: states,
	}
}

// Start runs every registered check once, synchronously, so the very
// first Report is accurate immediately, and then launches a background
// goroutine that re-runs every check on the configured interval. Start
// itself does not block on the background loop, and the loop keeps
// running independently of ctx: cancel it with Stop, not by canceling
// ctx.
func (checker *Checker) Start(ctx context.Context) error {
	checker.runAll(ctx)

	loopCtx, cancel := context.WithCancel(context.Background())
	checker.cancel = cancel
	checker.done = make(chan struct{})

	go checker.loop(loopCtx)

	return nil
}

// Stop cancels the background loop and waits for it to finish before
// returning, or until ctx is done, whichever comes first.
func (checker *Checker) Stop(ctx context.Context) error {
	if checker.cancel == nil {
		return nil
	}
	checker.cancel()

	select {
	case <-checker.done:
	case <-ctx.Done():
		return ctx.Err()
	}
	return nil
}

// loop re-runs every check on the configured interval until ctx is done.
func (checker *Checker) loop(ctx context.Context) {
	defer close(checker.done)

	ticker := time.NewTicker(checker.config.Interval)
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

// runAll runs every registered check once, sequentially, each bounded by
// the configured per-check timeout.
func (checker *Checker) runAll(ctx context.Context) {
	for _, check := range checker.checks {
		checker.run(ctx, check)
	}
}

// run executes one check under a timeout-bounded context and updates its
// state and metrics.
func (checker *Checker) run(ctx context.Context, check Check) {
	runCtx, cancel := context.WithTimeout(ctx, checker.config.Timeout)
	defer cancel()

	start := time.Now()
	err := check.Run(runCtx)
	duration := time.Since(start)

	checker.mutex.Lock()
	checkState := checker.states[check.Name]
	checkState.lastCheckedAt = start
	checkState.lastDuration = duration
	if err != nil {
		checkState.lastError = err
		checkState.consecutiveFailures++
		if checkState.consecutiveFailures >= checker.config.FailureThreshold {
			checkState.failing = true
		}
	} else {
		checkState.lastError = nil
		checkState.consecutiveFailures = 0
		checkState.failing = false
	}
	passing := !checkState.failing
	checker.mutex.Unlock()

	recordCheck(ctx, check.Name, duration, passing)
}

// Ready reports whether every registered check is currently passing. A
// Checker with no registered checks is always ready.
func (checker *Checker) Ready() bool {
	checker.mutex.Lock()
	defer checker.mutex.Unlock()

	for _, checkState := range checker.states {
		if checkState.failing {
			return false
		}
	}
	return true
}

// Report builds the current aggregate Report from every registered
// check's state.
func (checker *Checker) Report() Report {
	checker.mutex.Lock()
	defer checker.mutex.Unlock()

	report := Report{
		Status: StatusPass,
		Checks: make(map[string]CheckStatus, len(checker.states)),
	}

	for _, check := range checker.checks {
		checkState := checker.states[check.Name]
		status := checkState.snapshot()
		report.Checks[check.Name] = status
		if status.Status == StatusFail {
			report.Status = StatusFail
		}
	}

	return report
}
