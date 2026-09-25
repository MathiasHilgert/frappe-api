package telemetry

import (
	"testing"

	semconv "go.opentelemetry.io/otel/semconv/v1.30.0"
)

// TestBuildResourceCarriesServiceVersion proves that the resource Up
// builds carries settings.ServiceVersion as its service.version
// attribute. In production, settings.ServiceVersion is always
// internal/foundation/build.Version (the ldflags-stamped build identity,
// wired by internal/dependencies.telemetrySettingsFrom) rather than any
// configuration field, so this is the single source of truth for the
// exported service.version.
func TestBuildResourceCarriesServiceVersion(t *testing.T) {
	settings := Settings{
		ServiceName:           "frappe-api",
		ServiceVersion:        "9.9.9-from-ldflags",
		DeploymentEnvironment: "development",
	}

	detectedResource, err := buildResource(settings)
	if err != nil {
		t.Fatalf("buildResource returned unexpected error: %v", err)
	}

	got, ok := detectedResource.Set().Value(semconv.ServiceVersionKey)
	if !ok {
		t.Fatal("expected the resource to carry a service.version attribute")
	}
	if got.AsString() != settings.ServiceVersion {
		t.Fatalf("service.version = %q, want %q", got.AsString(), settings.ServiceVersion)
	}
}
