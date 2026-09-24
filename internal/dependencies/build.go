package dependencies

import (
	"context"
	"fmt"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/application"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/build"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/httpserver"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/logging"
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

	// Logging is installed first, always, independent of whether
	// telemetry is enabled, and before any hook runs (hooks only run once
	// instance.Up is called later, by application.Run). This guarantees
	// every log line the process produces, including lifecycle logs, is
	// JSON and level-gated from the very first line, regardless of
	// TELEMETRY_ENABLED. Logging must not depend on telemetry.
	loggingRuntime := logging.Up(newLoggingSettings(loadedConfiguration.Logging.Level))

	// The telemetry dependency is registered first among the hooks so it
	// is up before every other hook and down after every other hook,
	// keeping tracing, metrics and the log bridge available for the whole
	// lifecycle. When telemetry.Up succeeds and exposes a log handler
	// (telemetry is enabled), that handler is attached to the already
	// installed process logger's fan-out, so log records also reach the
	// OTel logs pipeline; when telemetry is disabled or Up fails, logging
	// keeps working on its own, JSON and level-gated.
	telemetrySettings := telemetrySettingsFrom(loadedConfiguration, build.Version)
	application.Provide(instance, application.Dependency[telemetry.SDK]{
		Name: telemetry.DependencyName,
		Up: func(ctx context.Context) (telemetry.SDK, error) {
			sdk, err := telemetry.Up(ctx, telemetrySettings)
			if err != nil {
				return telemetry.SDK{}, err
			}
			if handler := sdk.LogHandler(); handler != nil {
				loggingRuntime.Attach(handler)
			}
			return sdk, nil
		},
		Down: telemetry.Down,
	})

	// The HTTP server is built synchronously (not yet listening) so its
	// "/v1" huma.API is available immediately for modules to register
	// their own routes on as they are wired in below. It is registered
	// as a dependency after telemetry, so it comes up last (traces and
	// metrics are ready before it accepts traffic) and goes down first
	// (in-flight requests finish before telemetry flushes).
	server := httpserver.New(httpserver.Settings{
		Title:                loadedConfiguration.Application.Name,
		Version:              build.Version,
		Port:                 loadedConfiguration.HTTP.Port,
		ReadHeaderTimeout:    loadedConfiguration.HTTP.ReadHeaderTimeout,
		ReadTimeout:          loadedConfiguration.HTTP.ReadTimeout,
		WriteTimeout:         loadedConfiguration.HTTP.WriteTimeout,
		IdleTimeout:          loadedConfiguration.HTTP.IdleTimeout,
		MaxHeaderBytes:       loadedConfiguration.HTTP.MaxHeaderBytes,
		MaxBodyBytes:         loadedConfiguration.HTTP.MaxBodyBytes,
		DocumentationEnabled: loadedConfiguration.HTTP.DocumentationEnabled,
		Ready:                instance.Ready,
	})

	// No concrete module exists yet; each one, as it is added, gets
	// wired here with its own constructor call passing server.V1() and
	// instance.Use(...), following foundation/httpserver's doc.go
	// convention.

	application.Provide(instance, application.Dependency[*httpserver.Server]{
		Name: httpserver.DependencyName,
		Up: func(context.Context) (*httpserver.Server, error) {
			return server, server.Listen()
		},
		Down: func(ctx context.Context, server *httpserver.Server) error {
			return server.Shutdown(ctx)
		},
	})

	return instance, nil
}

// newLoggingSettings builds the logging.Settings the composition root
// installs from the configured logging level. It is a package-level
// variable, rather than a direct call to logging.Settings{...}, only so
// tests can override it to inject a writer other than the default
// os.Stdout and observe what NewApplication logs, without changing
// NewApplication's signature.
var newLoggingSettings = func(level string) logging.Settings {
	return logging.Settings{Level: level}
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
	}
}
