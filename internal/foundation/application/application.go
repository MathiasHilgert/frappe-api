package application

import (
	"context"
	"errors"
	"log/slog"
	"sync/atomic"
	"time"
)

// Application owns a set of hooks and starts or stops them together,
// honoring registration order on Up and the reverse order on Down.
type Application struct {
	// failure carries an error reported through Fail into Run's shutdown
	// wait. It is buffered by one so Fail never blocks its caller, even
	// if Run has not reached its wait yet or has already returned.
	// Declared first (not next to Fail below) so the struct's pointer
	// fields stay grouped for optimal field alignment.
	failure chan error
	hooks   []Hook
	checks  []Check
	options options
	ready   atomic.Bool
}

// New creates an Application configured by the given options.
func New(optionFunctions ...Option) *Application {
	resolvedOptions := newOptions(optionFunctions)

	return &Application{
		options: resolvedOptions,
		failure: make(chan error, 1),
	}
}

// Append registers hook to be started on Up and stopped on Down. Hooks run
// in the order they are appended, and stop in the reverse order.
func (application *Application) Append(hook Hook) {
	application.hooks = append(application.hooks, hook)
}

// AppendCheck registers check to be exposed later through Checks. It
// implements CheckRegistry so Provide can collect a Dependency's declared
// Check alongside its Hook.
func (application *Application) AppendCheck(check Check) {
	application.checks = append(application.checks, check)
}

// Checks returns every health check registered so far by dependencies
// that declared a Check function, in registration order.
func (application *Application) Checks() []Check {
	return application.checks
}

// Up starts every registered hook in registration order. If a hook's Up
// call fails, Up stops starting further hooks, tears down every hook that
// already started (in reverse order), and returns a joined error combining
// the original failure with any error produced during that rollback.
func (application *Application) Up(ctx context.Context) error {
	started := make([]Hook, 0, len(application.hooks))

	for _, hook := range application.hooks {
		if err := application.runPhase(ctx, hook, hook.Up, "up"); err != nil {
			rollbackErr := application.tearDown(ctx, started)
			return errors.Join(err, rollbackErr)
		}
		started = append(started, hook)
	}

	// Recorded here, once every hook (including telemetry's own Up, which
	// installs the real MeterProvider) has succeeded, so this gauge is
	// exported through the SDK it depends on rather than lost to the
	// still-noop delegate that is in place during New.
	if application.options.buildInfo.set {
		recordBuildInfo(ctx, application.options.buildInfo.version, application.options.buildInfo.commit, application.options.buildInfo.environment)
	}

	application.ready.Store(true)
	recordReady(ctx, 1)

	return nil
}

// Down stops every registered hook in reverse registration order. It never
// stops early: every hook's Down is attempted, and every resulting error is
// combined into one joined error.
func (application *Application) Down(ctx context.Context) error {
	application.ready.Store(false)
	recordReady(ctx, 0)
	return application.tearDown(ctx, application.hooks)
}

// Fail requests that the Application shut down early because a
// dependency hit a fatal error after Up already completed, such as
// httpserver.Server's Listen goroutine observing its listener break
// outside of a graceful Shutdown. It is meant to be called from that
// dependency's own goroutine, asynchronously, at any point during or
// after Run: Run reacts to it exactly like a shutdown signal, then
// returns err joined with any error Down produces. Only the first call
// takes effect; a nil err, or a call after one already landed, is a
// no-op rather than blocking or overwriting the first failure. Fail does
// not itself flip Ready to false: that already happens as soon as the
// resulting Down begins.
func (application *Application) Fail(err error) {
	if err == nil {
		return
	}
	select {
	case application.failure <- err:
	default:
	}
}

// Ready reports whether every registered hook's Up has completed
// successfully and Down has not started yet. It is safe to call
// concurrently, so it can back an HTTP readiness probe while Up or Down
// runs on another goroutine.
func (application *Application) Ready() bool {
	return application.ready.Load()
}

// tearDown runs Down for the given hooks in reverse order, continuing past
// failures and joining every error encountered.
func (application *Application) tearDown(ctx context.Context, hooks []Hook) error {
	var errs []error
	for index := len(hooks) - 1; index >= 0; index-- {
		hook := hooks[index]
		if err := application.runPhase(ctx, hook, hook.Down, "down"); err != nil {
			errs = append(errs, err)
		}
	}
	return errors.Join(errs...)
}

// runPhase invokes the given hook function, if any, under a context bounded
// by the configured per-hook timeout, and logs the outcome.
func (application *Application) runPhase(ctx context.Context, hook Hook, phaseFunction func(context.Context) error, phase string) error {
	if phaseFunction == nil {
		return nil
	}

	phaseCtx, cancel := context.WithTimeout(ctx, application.options.hookTimeout)
	defer cancel()

	start := time.Now()
	err := phaseFunction(phaseCtx)
	duration := time.Since(start)

	application.logPhase(hook.Name, phase, duration, err)
	recordHookPhase(ctx, hook.Name, phase, duration, err)

	return err
}

// logPhase records the outcome of one hook phase through the resolved
// logger. duration is reported as duration_milliseconds, a float64,
// matching the httpserver access log convention, rather than a raw
// time.Duration nanosecond count.
func (application *Application) logPhase(name, phase string, duration time.Duration, err error) {
	logger := application.resolveLogger()
	durationMilliseconds := float64(duration) / float64(time.Millisecond)

	if err != nil {
		logger.Error("hook phase failed", slog.String("hook", name), slog.String("phase", phase), slog.Float64("duration_milliseconds", durationMilliseconds), slog.Any("error", err))
		return
	}

	logger.Info("hook phase completed", slog.String("hook", name), slog.String("phase", phase), slog.Float64("duration_milliseconds", durationMilliseconds))
}

// resolveLogger returns the explicit logger given through WithLogger, if
// any, or slog.Default() otherwise. It is called at the time each hook
// phase logs rather than once at New, so lifecycle logs still reach the
// slog default logger installed by a hook that runs before others, such
// as telemetry's own Up.
func (application *Application) resolveLogger() *slog.Logger {
	if application.options.logger != nil {
		return application.options.logger
	}
	return slog.Default()
}
