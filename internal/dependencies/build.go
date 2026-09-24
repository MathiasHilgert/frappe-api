package dependencies

import (
	"context"
	"fmt"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/application"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/telemetry"
)

// NewApplication loads the application-wide configuration through
// provider and builds the Application, wiring every concrete module into
// it. It is the single place that knows the full set of modules the
// running program uses.
func NewApplication(ctx context.Context, provider configuration.Provider) (*application.Application, error) {
	loadedConfiguration, err := provider.Load(ctx)
	if err != nil {
		return nil, fmt.Errorf("load configuration: %w", err)
	}

	instance := application.New(application.WithHookTimeout(loadedConfiguration.Application.HookTimeout))

	// The telemetry dependency is registered first so it is up before
	// every other hook and down after every other hook, keeping tracing,
	// metrics and the log bridge available for the whole lifecycle.
	telemetrySettings := telemetry.Settings{
		Enabled:               loadedConfiguration.Telemetry.Enabled,
		ServiceName:           loadedConfiguration.Application.Name,
		ServiceVersion:        loadedConfiguration.Application.Version,
		DeploymentEnvironment: loadedConfiguration.Application.Environment,
	}
	application.Provide(instance, application.Dependency[telemetry.SDK]{
		Name: telemetry.DependencyName,
		Up: func(ctx context.Context) (telemetry.SDK, error) {
			return telemetry.Up(ctx, telemetrySettings)
		},
		Down: telemetry.Down,
	})

	// No other concrete module exists yet; modules will be registered
	// here with instance.Use(...) as they are added.

	return instance, nil
}
