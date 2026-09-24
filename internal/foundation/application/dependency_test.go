package application_test

import (
	"context"
	"errors"
	"testing"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/application"
)

// TestProvideMakesDependencyValueAvailableAfterUp verifies that a
// dependency registered through Provide is started during Up and its value
// becomes readable from the returned handle afterward.
func TestProvideMakesDependencyValueAvailableAfterUp(t *testing.T) {
	instance := application.New()

	handle := application.Provide(instance, application.Dependency[string]{
		Name: "greeting",
		Up: func(context.Context) (string, error) {
			return "hello", nil
		},
	})

	if err := instance.Up(context.Background()); err != nil {
		t.Fatalf("Up returned unexpected error: %v", err)
	}

	value, ready := handle.Get()
	if !ready {
		t.Fatal("handle is not ready after Up")
	}
	if value != "hello" {
		t.Fatalf("handle value = %q, want %q", value, "hello")
	}
}

// TestProvideDependencyDownReceivesTheUpValue verifies that a dependency's
// Down function receives the exact value produced by its Up function.
func TestProvideDependencyDownReceivesTheUpValue(t *testing.T) {
	instance := application.New()

	var receivedOnDown int
	application.Provide(instance, application.Dependency[int]{
		Name: "counter",
		Up: func(context.Context) (int, error) {
			return 42, nil
		},
		Down: func(_ context.Context, value int) error {
			receivedOnDown = value
			return nil
		},
	})

	if err := instance.Up(context.Background()); err != nil {
		t.Fatalf("Up returned unexpected error: %v", err)
	}
	if err := instance.Down(context.Background()); err != nil {
		t.Fatalf("Down returned unexpected error: %v", err)
	}

	if receivedOnDown != 42 {
		t.Fatalf("Down received %d, want 42", receivedOnDown)
	}
}

// TestHandleGetBeforeUpReportsNotReady verifies that reading a handle
// before Up has run reports that no value is available yet, instead of
// returning a zero value silently.
func TestHandleGetBeforeUpReportsNotReady(t *testing.T) {
	instance := application.New()

	handle := application.Provide(instance, application.Dependency[int]{
		Name: "counter",
		Up: func(context.Context) (int, error) {
			return 7, nil
		},
	})

	if _, ready := handle.Get(); ready {
		t.Fatal("handle reports ready before Up ran")
	}
}

// TestModuleRegisterAddsHooksThroughUse verifies that Use registers each
// module's hooks by calling its Register method with the Application as
// the Lifecycle.
func TestModuleRegisterAddsHooksThroughUse(t *testing.T) {
	instance := application.New()

	started := false
	module := stubModule{
		name: "stub",
		register: func(lifecycle application.Lifecycle) error {
			lifecycle.Append(application.Hook{
				Name: "stub-hook",
				Up: func(context.Context) error {
					started = true
					return nil
				},
			})
			return nil
		},
	}

	if err := instance.Use(module); err != nil {
		t.Fatalf("Use returned unexpected error: %v", err)
	}
	if err := instance.Up(context.Background()); err != nil {
		t.Fatalf("Up returned unexpected error: %v", err)
	}
	if !started {
		t.Fatal("module hook was never started")
	}
}

// TestUseReturnsModuleRegisterError verifies that Use propagates an error
// returned by a module's Register method instead of silently ignoring it.
func TestUseReturnsModuleRegisterError(t *testing.T) {
	instance := application.New()

	registerErr := errors.New("registration failed")
	module := stubModule{
		name:     "broken",
		register: func(application.Lifecycle) error { return registerErr },
	}

	err := instance.Use(module)
	if !errors.Is(err, registerErr) {
		t.Fatalf("Use error = %v, want it to wrap %v", err, registerErr)
	}
}

// TestApplicationRunStopsWhenContextIsCanceled verifies that Run starts the
// application, waits until the given context is canceled, and then tears
// everything down before returning.
func TestApplicationRunStopsWhenContextIsCanceled(t *testing.T) {
	instance := application.New()

	var downCalled bool
	instance.Append(application.Hook{
		Name: "watched",
		Down: func(context.Context) error {
			downCalled = true
			return nil
		},
	})

	ctx, cancel := context.WithCancel(context.Background())
	cancel()

	if err := instance.Run(ctx); err != nil {
		t.Fatalf("Run returned unexpected error: %v", err)
	}
	if !downCalled {
		t.Fatal("Run did not tear down hooks after context cancellation")
	}
}

// stubModule is a minimal Module implementation used to exercise Use.
type stubModule struct {
	register func(application.Lifecycle) error
	name     string
}

func (module stubModule) Name() string {
	return module.name
}

func (module stubModule) Register(lifecycle application.Lifecycle) error {
	return module.register(lifecycle)
}
