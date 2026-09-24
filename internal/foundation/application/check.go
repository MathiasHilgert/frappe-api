package application

import "context"

// Check is one named health check collected from a Dependency that
// declared a Check function. Run executes the check against the
// dependency's current value.
type Check struct {
	// Run executes the check, returning an error if the dependency is
	// unhealthy.
	Run func(ctx context.Context) error
	// Name identifies the dependency this check belongs to.
	Name string
}

// CheckRegistry collects health checks declared by dependencies. An
// Application implements CheckRegistry in addition to Lifecycle, so
// Provide can register a Check alongside the Hook it always registers.
type CheckRegistry interface {
	// AppendCheck registers check to be exposed later through Checks.
	AppendCheck(check Check)
}

// registerCheck registers a named check against lifecycle if lifecycle
// also implements CheckRegistry. A Lifecycle implementation that does not
// implement CheckRegistry simply never collects checks, which keeps
// Provide usable against minimal test doubles.
func registerCheck(lifecycle Lifecycle, name string, run func(ctx context.Context) error) {
	registry, ok := lifecycle.(CheckRegistry)
	if !ok {
		return
	}
	registry.AppendCheck(Check{Name: name, Run: run})
}
