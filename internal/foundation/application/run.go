package application

import (
	"context"
	"os"
	"os/signal"
	"syscall"
)

// defaultSignals are the operating system signals Run treats as a shutdown
// request when no signals were configured through WithSignals.
var defaultSignals = []os.Signal{os.Interrupt, syscall.SIGTERM}

// Run starts the application, waits until ctx is canceled or one of the
// configured shutdown signals arrives, and then stops the application
// using a fresh context so shutdown is not cut short by ctx already being
// done. It returns the joined error of Up and Down, if any.
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

	select {
	case <-ctx.Done():
	case <-signalCtx.Done():
	}

	return application.Down(context.Background())
}
