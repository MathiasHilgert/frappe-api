package dependencies

import (
	"context"
	"fmt"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/application"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/build"
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

	instance := application.New(
		application.WithHookTimeout(loadedConfiguration.Application.HookTimeout),
		application.WithBuildInfo(build.Version, build.Commit, loadedConfiguration.Application.Environment),
	)

	// The telemetry dependency is registered first so it is up before
	// every other hook and down after every other hook, keeping tracing,
	// metrics and the log bridge available for the whole lifecycle.
	telemetrySettings := telemetrySettingsFrom(loadedConfiguration, build.Version)
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

// telemetrySettingsFrom builds the telemetry.Settings the composition root
// installs, given loadedConfiguration and version. version is always
// internal/foundation/build.Version (the ldflags-stamped build identity)
// in production code: it is a parameter, rather than a direct reference to
// the build package, only so this mapping stays a small, pure, directly
// testable function. There is a single source of truth for the exported
// service.version: build.Version, never a configuration field.
func telemetrySettingsFrom(loadedConfiguration configuration.Configuration, version string) telemetry.Settings {
	return telemetry.Settings{
		Enabled:               loadedConfiguration.Telemetry.Enabled,
		ServiceName:           loadedConfiguration.Application.Name,
		ServiceVersion:        version,
		DeploymentEnvironment: loadedConfiguration.Application.Environment,
		LoggingLevel:          loadedConfiguration.Logging.Level,
	}
}
