package application

import (
	"context"
	"errors"
	"os"
	"os/signal"
	"syscall"
)

// defaultSignals are the operating system signals Run treats as a shutdown
// request when no signals were configured through WithSignals.
var defaultSignals = []os.Signal{os.Interrupt, syscall.SIGTERM}

// Run starts the application, then waits until ctx is canceled, one of the
// configured shutdown signals arrives, or a dependency reports a fatal
// error through Fail, and then stops the application using a fresh
// context so shutdown is not cut short by ctx already being done. It
// returns the joined error of Up, any error passed to Fail, and Down, if
// any.
func (application *Application) Run(ctx context.Context) error {
	if err := application.Up(ctx); err != nil {
		return err
	}

	signals := application.options.signals
	if len(signals) == 0 {
		signals = defaultSignals
	}

	signalCtx, stop := signal.NotifyContext(context.Background(), signals...)
	defer stop()

	var failureErr error
	select {
	case <-ctx.Done():
	case <-signalCtx.Done():
	case failureErr = <-application.failure:
	}

	downErr := application.Down(context.Background())
	return errors.Join(failureErr, downErr)
}
