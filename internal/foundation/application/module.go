package application

// Module is a self-contained unit of the application that declares its own
// hooks against a Lifecycle instead of being wired together through
// reflection-based dependency injection.
type Module interface {
	// Name identifies the module for logging and error reporting.
	Name() string
	// Register appends the module's hooks onto lifecycle.
	Register(lifecycle Lifecycle) error
}

// Use registers every given module by calling its Register method with the
// Application as the Lifecycle. If a module fails to register, Use returns
// that error immediately without registering the remaining modules.
func (application *Application) Use(modules ...Module) error {
	for _, module := range modules {
		if err := module.Register(application); err != nil {
			return err
		}
	}
	return nil
}
