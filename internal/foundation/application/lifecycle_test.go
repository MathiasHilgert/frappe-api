package application_test

import (
	"context"
	"errors"
	"testing"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/application"
)

// TestApplicationUpRunsHooksInRegistrationOrder verifies that Up invokes
// every hook's Up function in the exact order the hooks were appended.
func TestApplicationUpRunsHooksInRegistrationOrder(t *testing.T) {
	application_ := application.New()

	var order []string
	application_.Append(application.Hook{
		Name: "first",
		Up: func(context.Context) error {
			order = append(order, "first")
			return nil
		},
	})
	application_.Append(application.Hook{
		Name: "second",
		Up: func(context.Context) error {
			order = append(order, "second")
			return nil
		},
	})
	application_.Append(application.Hook{
		Name: "third",
		Up: func(context.Context) error {
			order = append(order, "third")
			return nil
		},
	})

	if err := application_.Up(context.Background()); err != nil {
		t.Fatalf("Up returned unexpected error: %v", err)
	}

	want := []string{"first", "second", "third"}
	if len(order) != len(want) {
		t.Fatalf("order = %v, want %v", order, want)
	}
	for index, name := range want {
		if order[index] != name {
			t.Fatalf("order = %v, want %v", order, want)
		}
	}
}

// TestApplicationUpRollsBackOnlyStartedHooksOnFailure verifies that when a
// hook's Up fails, only the hooks that already started are torn down, in
// reverse order, and hooks that never started are left untouched.
func TestApplicationUpRollsBackOnlyStartedHooksOnFailure(t *testing.T) {
	application_ := application.New()

	var downOrder []string
	failing := errors.New("second hook failed")
	thirdUpCalled := false

	application_.Append(application.Hook{
		Name: "first",
		Up:   func(context.Context) error { return nil },
		Down: func(context.Context) error {
			downOrder = append(downOrder, "first")
			return nil
		},
	})
	application_.Append(application.Hook{
		Name: "second",
		Up:   func(context.Context) error { return failing },
		Down: func(context.Context) error {
			downOrder = append(downOrder, "second")
			return nil
		},
	})
	application_.Append(application.Hook{
		Name: "third",
		Up: func(context.Context) error {
			thirdUpCalled = true
			return nil
		},
		Down: func(context.Context) error {
			downOrder = append(downOrder, "third")
			return nil
		},
	})

	err := application_.Up(context.Background())
	if err == nil {
		t.Fatal("Up returned nil error, want the Up failure")
	}
	if !errors.Is(err, failing) {
		t.Fatalf("Up error = %v, want it to wrap %v", err, failing)
	}
	if thirdUpCalled {
		t.Fatal("third hook Up was called, want it never started")
	}

	want := []string{"first"}
	if len(downOrder) != len(want) || downOrder[0] != want[0] {
		t.Fatalf("downOrder = %v, want %v (only the started hook, in reverse)", downOrder, want)
	}
}

// TestApplicationDownRunsHooksInReverseOrder verifies that Down tears down
// hooks in the reverse of their registration order.
func TestApplicationDownRunsHooksInReverseOrder(t *testing.T) {
	application_ := application.New()

	var order []string
	application_.Append(application.Hook{
		Name: "first",
		Down: func(context.Context) error {
			order = append(order, "first")
			return nil
		},
	})
	application_.Append(application.Hook{
		Name: "second",
		Down: func(context.Context) error {
			order = append(order, "second")
			return nil
		},
	})

	if err := application_.Down(context.Background()); err != nil {
		t.Fatalf("Down returned unexpected error: %v", err)
	}

	want := []string{"second", "first"}
	if len(order) != len(want) {
		t.Fatalf("order = %v, want %v", order, want)
	}
	for index, name := range want {
		if order[index] != name {
			t.Fatalf("order = %v, want %v", order, want)
		}
	}
}

// TestApplicationDownAggregatesAllErrorsWithoutStoppingEarly verifies that
// Down never stops at the first failing hook and returns every error
// joined together.
func TestApplicationDownAggregatesAllErrorsWithoutStoppingEarly(t *testing.T) {
	application_ := application.New()

	firstError := errors.New("first hook down failed")
	secondError := errors.New("second hook down failed")
	thirdCalled := false

	application_.Append(application.Hook{
		Name: "first",
		Down: func(context.Context) error { return firstError },
	})
	application_.Append(application.Hook{
		Name: "second",
		Down: func(context.Context) error { return secondError },
	})
	application_.Append(application.Hook{
		Name: "third",
		Down: func(context.Context) error {
			thirdCalled = true
			return nil
		},
	})

	err := application_.Down(context.Background())
	if err == nil {
		t.Fatal("Down returned nil error, want the joined errors")
	}
	if !errors.Is(err, firstError) {
		t.Fatalf("Down error = %v, want it to wrap %v", err, firstError)
	}
	if !errors.Is(err, secondError) {
		t.Fatalf("Down error = %v, want it to wrap %v", err, secondError)
	}
	if !thirdCalled {
		t.Fatal("third hook Down was not called, want Down to continue past earlier failures")
	}
}

// TestApplicationPerHookTimeoutCancelsSlowHook verifies that each hook runs
// under its own timeout derived from the configured shutdown timeout, and a
// hook that outlives it receives a canceled context.
func TestApplicationPerHookTimeoutCancelsSlowHook(t *testing.T) {
	application_ := application.New(application.WithHookTimeout(10 * time.Millisecond))

	var observedError error
	application_.Append(application.Hook{
		Name: "slow",
		Down: func(ctx context.Context) error {
			select {
			case <-time.After(200 * time.Millisecond):
				return nil
			case <-ctx.Done():
				observedError = ctx.Err()
				return ctx.Err()
			}
		},
	})

	err := application_.Down(context.Background())
	if err == nil {
		t.Fatal("Down returned nil error, want the hook timeout error")
	}
	if observedError == nil {
		t.Fatal("hook context was never canceled by the per-hook timeout")
	}
}

// TestApplicationHookWithNilUpAndDownIsSkippedSafely verifies that a hook
// with a nil Up or Down function is simply skipped instead of panicking.
func TestApplicationHookWithNilUpAndDownIsSkippedSafely(t *testing.T) {
	application_ := application.New()

	application_.Append(application.Hook{Name: "no-op"})

	if err := application_.Up(context.Background()); err != nil {
		t.Fatalf("Up returned unexpected error: %v", err)
	}
	if err := application_.Down(context.Background()); err != nil {
		t.Fatalf("Down returned unexpected error: %v", err)
	}
}
