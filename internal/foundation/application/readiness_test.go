package application_test

import (
	"context"
	"testing"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/application"
)

// TestReadyIsFalseBeforeUp verifies that a freshly created Application is
// not ready until Up has completed.
func TestReadyIsFalseBeforeUp(t *testing.T) {
	instance := application.New()

	if instance.Ready() {
		t.Fatal("Ready() = true before Up ran")
	}
}

// TestReadyIsTrueAfterSuccessfulUp verifies that Ready reports true once
// every hook's Up has succeeded.
func TestReadyIsTrueAfterSuccessfulUp(t *testing.T) {
	instance := application.New()

	if err := instance.Up(context.Background()); err != nil {
		t.Fatalf("Up returned unexpected error: %v", err)
	}

	if !instance.Ready() {
		t.Fatal("Ready() = false after a successful Up")
	}
}

// TestReadyIsFalseAfterDown verifies that Ready reports false again once
// Down starts, so health checks stop reporting readiness during shutdown.
func TestReadyIsFalseAfterDown(t *testing.T) {
	instance := application.New()

	if err := instance.Up(context.Background()); err != nil {
		t.Fatalf("Up returned unexpected error: %v", err)
	}
	if err := instance.Down(context.Background()); err != nil {
		t.Fatalf("Down returned unexpected error: %v", err)
	}

	if instance.Ready() {
		t.Fatal("Ready() = true after Down")
	}
}

// TestReadyIsFalseWhenAHookFailsToStart verifies that Ready never reports
// true when Up failed partway through.
func TestReadyIsFalseWhenAHookFailsToStart(t *testing.T) {
	instance := application.New()

	instance.Append(application.Hook{
		Name: "broken",
		Up: func(context.Context) error {
			return errBroken
		},
	})

	if err := instance.Up(context.Background()); err == nil {
		t.Fatal("Up returned nil error for a failing hook")
	}

	if instance.Ready() {
		t.Fatal("Ready() = true after a failed Up")
	}
}

var errBroken = errBrokenType{}

type errBrokenType struct{}

func (errBrokenType) Error() string { return "broken" }
