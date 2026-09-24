package application

import (
	"context"
	"errors"
	"log/slog"
	"time"
)

// Application owns a set of hooks and starts or stops them together,
// honoring registration order on Up and the reverse order on Down.
type Application struct {
	hooks   []Hook
	options options
}

// New creates an Application configured by the given options.
func New(optionFunctions ...Option) *Application {
	resolvedOptions := newOptions(optionFunctions)

	if resolvedOptions.buildInfo.set {
		recordBuildInfo(context.Background(), resolvedOptions.buildInfo.version, resolvedOptions.buildInfo.commit, resolvedOptions.buildInfo.environment)
	}

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

	recordReady(ctx, 1)

	return nil
}

// Down stops every registered hook in reverse registration order. It never
// stops early: every hook's Down is attempted, and every resulting error is
// combined into one joined error.
func (application *Application) Down(ctx context.Context) error {
	recordReady(ctx, 0)
	return application.tearDown(ctx, application.hooks)
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

// logPhase records the outcome of one hook phase through the configured
// logger.
func (application *Application) logPhase(name, phase string, duration time.Duration, err error) {
	logger := application.options.logger
	if logger == nil {
		return
	}

	if err != nil {
		logger.Error("hook phase failed", slog.String("hook", name), slog.String("phase", phase), slog.Duration("duration", duration), slog.Any("error", err))
		return
	}

	logger.Info("hook phase completed", slog.String("hook", name), slog.String("phase", phase), slog.Duration("duration", duration))
}
