// Package application provides a hook-based lifecycle for wiring modules and
// dependencies together explicitly, without reflection-based dependency
// injection. An Application collects Hook values through a Lifecycle,
// starts them in registration order on Up, and tears them down in reverse
// order on Down.
package application

import "context"

// Hook is one named unit of startup and shutdown behavior. Up starts the
// unit; Down releases it. Either function may be nil, in which case that
// phase is skipped for the hook.
type Hook struct {
	// Up starts the hook. It may be nil if the hook has nothing to start.
	Up func(ctx context.Context) error
	// Down releases the hook. It may be nil if the hook has nothing to release.
	Down func(ctx context.Context) error
	// Name identifies the hook for logging and error reporting.
	Name string
}

// Lifecycle collects hooks that an Application will start and stop.
// Modules and dependencies register their hooks against a Lifecycle instead
// of managing their own startup and shutdown ordering.
type Lifecycle interface {
	// Append registers hook to be started and stopped by the Application
	// that owns this Lifecycle.
	Append(hook Hook)
}
