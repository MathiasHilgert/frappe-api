package dependencies

import (
	"testing"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
)

// TestTelemetrySettingsFromUsesTheGivenVersionNotConfiguration proves that
// the telemetry resource's service.version comes from a single source of
// truth (the version argument, which NewApplication feeds from
// internal/foundation/build.Version, the ldflags-stamped build identity)
// rather than from configuration.Application, which no longer declares a
// Version field at all.
func TestTelemetrySettingsFromUsesTheGivenVersionNotConfiguration(t *testing.T) {
	loadedConfiguration := configuration.Configuration{
		Application: configuration.Application{
			Name:        "frappe-api",
			Environment: "development",
			HookTimeout: 5 * time.Second,
		},
		HTTP: configuration.HTTP{Port: 8080},
		Logging: configuration.Logging{
			Level: "info",
		},
		Telemetry: configuration.Telemetry{
			Enabled: true,
		},
	}

	settings := telemetrySettingsFrom(loadedConfiguration, "9.9.9-from-ldflags")

	if settings.ServiceVersion != "9.9.9-from-ldflags" {
		t.Fatalf("settings.ServiceVersion = %q, want %q", settings.ServiceVersion, "9.9.9-from-ldflags")
	}
	if settings.ServiceName != loadedConfiguration.Application.Name {
		t.Fatalf("settings.ServiceName = %q, want %q", settings.ServiceName, loadedConfiguration.Application.Name)
	}
	if settings.DeploymentEnvironment != loadedConfiguration.Application.Environment {
		t.Fatalf("settings.DeploymentEnvironment = %q, want %q", settings.DeploymentEnvironment, loadedConfiguration.Application.Environment)
	}
	if settings.Enabled != loadedConfiguration.Telemetry.Enabled {
		t.Fatalf("settings.Enabled = %v, want %v", settings.Enabled, loadedConfiguration.Telemetry.Enabled)
	}
}
