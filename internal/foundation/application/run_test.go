package application_test

import (
	"context"
	"errors"
	"testing"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/application"
)

// TestFailTriggersRunToShutDownAndReturnTheError verifies that Fail lets a
// dependency (such as httpserver.Server observing its listener break)
// request an early shutdown after Up has already completed, instead of
// Run only ever reacting to context cancellation or OS signals. Run must
// return the failure error and Ready must report false once shutdown
// begins.
func TestFailTriggersRunToShutDownAndReturnTheError(t *testing.T) {
	instance := application.New()

	downCalled := make(chan struct{})
	instance.Append(application.Hook{
		Name: "hook",
		Up:   func(context.Context) error { return nil },
		Down: func(context.Context) error {
			close(downCalled)
			return nil
		},
	})

	wantErr := errors.New("listener broke")

	runErr := make(chan error, 1)
	go func() {
		runErr <- instance.Run(context.Background())
	}()

	// Give Run a moment to reach Up and enter its shutdown wait before
	// failing it; Fail is documented to be safe to call at any point, but
	// this keeps the test deterministic about which path it exercises.
	select {
	case <-downCalled:
		t.Fatal("Down ran before Fail was ever called")
	case <-time.After(20 * time.Millisecond):
	}

	instance.Fail(wantErr)

	select {
	case <-downCalled:
	case <-time.After(2 * time.Second):
		t.Fatal("timed out waiting for Fail to trigger Down")
	}

	select {
	case err := <-runErr:
		if !errors.Is(err, wantErr) {
			t.Fatalf("Run error = %v, want it to wrap %v", err, wantErr)
		}
	case <-time.After(2 * time.Second):
		t.Fatal("timed out waiting for Run to return")
	}

	if instance.Ready() {
		t.Fatal("Ready() is still true after Fail triggered shutdown")
	}
}

// TestFailWithNilErrorDoesNothing verifies that Fail(nil) is a no-op:
// calling it does not spuriously trigger a shutdown.
func TestFailWithNilErrorDoesNothing(t *testing.T) {
	instance := application.New()
	instance.Append(application.Hook{
		Name: "hook",
		Up:   func(context.Context) error { return nil },
	})

	instance.Fail(nil)

	ctx, cancel := context.WithTimeout(context.Background(), 50*time.Millisecond)
	defer cancel()

	if err := instance.Run(ctx); err != nil {
		t.Fatalf("Run returned unexpected error: %v", err)
	}
}
