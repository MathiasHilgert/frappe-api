package application

import "context"

// Dependency describes one typed value whose lifecycle is managed by an
// Application: Up produces the value, and Down releases it.
type Dependency[T any] struct {
	// Up produces the dependency's value. It may be nil if the dependency
	// has nothing to start.
	Up func(ctx context.Context) (T, error)
	// Down releases the dependency's value. It may be nil if the
	// dependency has nothing to release.
	Down func(ctx context.Context, value T) error
	// Check reports whether the dependency's produced value is healthy.
	// It is optional: a dependency is health-checked if and only if it
	// declares a Check. There is no separate "critical" flag; every
	// declared check affects readiness.
	Check func(ctx context.Context, value T) error
	// Name identifies the dependency for logging and error reporting.
	Name string
}

// Handle exposes the value produced by a Dependency once its Up has run.
// A Handle is only ever written once, by the hook Provide registers, so
// reading it after Up completed is safe without further synchronization.
type Handle[T any] struct {
	value T
	ready bool
}

// Get returns the dependency's value and whether it is ready. ready is
// false until the owning Application's Up has run this dependency's hook.
func (handle *Handle[T]) Get() (T, bool) {
	return handle.value, handle.ready
}

// Provide registers dependency as a hook on lifecycle and returns a handle
// that exposes the produced value once the Application's Up has run. If
// dependency declares a Check, it is also registered as a named health
// check that closes over the value stored in handle.
func Provide[T any](lifecycle Lifecycle, dependency Dependency[T]) *Handle[T] {
	handle := &Handle[T]{}

	lifecycle.Append(Hook{
		Name: dependency.Name,
		Up:   dependencyUpHook(dependency, handle),
		Down: dependencyDownHook(dependency, handle),
	})

	if dependency.Check != nil {
		registerCheck(lifecycle, dependency.Name, func(ctx context.Context) error {
			return dependency.Check(ctx, handle.value)
		})
	}

	return handle
}

// dependencyUpHook adapts a Dependency's Up function into a Hook's Up
// function, storing the produced value into handle.
func dependencyUpHook[T any](dependency Dependency[T], handle *Handle[T]) func(context.Context) error {
	if dependency.Up == nil {
		return nil
	}
	return func(ctx context.Context) error {
		value, err := dependency.Up(ctx)
		if err != nil {
			return err
		}
		handle.value = value
		handle.ready = true
		return nil
	}
}

// dependencyDownHook adapts a Dependency's Down function into a Hook's
// Down function, passing it the value stored in handle.
func dependencyDownHook[T any](dependency Dependency[T], handle *Handle[T]) func(context.Context) error {
	if dependency.Down == nil {
		return nil
	}
	return func(ctx context.Context) error {
		return dependency.Down(ctx, handle.value)
	}
}
