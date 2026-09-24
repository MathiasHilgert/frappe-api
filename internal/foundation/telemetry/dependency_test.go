package telemetry

import (
	"context"
	"testing"

	"go.opentelemetry.io/otel"
)

func TestUpDownWhenDisabledKeepsNoopGlobalProviders(t *testing.T) {
	settings := Settings{
		Enabled:               false,
		ServiceName:           "frappe-api",
		ServiceVersion:        "0.0.0",
		DeploymentEnvironment: "development",
	}

	value, err := Up(context.Background(), settings)
	if err != nil {
		t.Fatalf("Up returned unexpected error: %v", err)
	}

	if otel.GetTracerProvider() == nil {
		t.Fatalf("expected a non-nil global tracer provider")
	}

	if err := Down(context.Background(), value); err != nil {
		t.Fatalf("Down returned unexpected error: %v", err)
	}
}

func TestDependencyNameIsSet(t *testing.T) {
	if DependencyName == "" {
		t.Fatalf("expected a non-empty dependency name")
	}
}
