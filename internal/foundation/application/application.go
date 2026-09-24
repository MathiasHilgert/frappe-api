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
	hooks   []Hook
	options options
	ready   atomic.Bool
}

// New creates an Application configured by the given options.
func New(optionFunctions ...Option) *Application {
	resolvedOptions := newOptions(optionFunctions)

	return &Application{
		options: resolvedOptions,
	}
}

// Append registers hook to be started on Up and stopped on Down. Hooks run
// in the order they are appended, and stop in the reverse order.
func (application *Application) Append(hook Hook) {
	application.hooks = append(application.hooks, hook)
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
// logger.
func (application *Application) logPhase(name, phase string, duration time.Duration, err error) {
	logger := application.resolveLogger()

	if err != nil {
		logger.Error("hook phase failed", slog.String("hook", name), slog.String("phase", phase), slog.Duration("duration", duration), slog.Any("error", err))
		return
	}

	logger.Info("hook phase completed", slog.String("hook", name), slog.String("phase", phase), slog.Duration("duration", duration))
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
