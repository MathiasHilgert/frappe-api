package environment_test

import (
	"context"
	"strings"
	"testing"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration/environment"
)

// requiredEnvironmentVariable is the one environment variable that has no
// default and must be set for Load to succeed.
const requiredEnvironmentVariable = "APPLICATION_ENVIRONMENT"

func TestLoadAppliesDefaultsWhenOnlyTheRequiredVariableIsSet(t *testing.T) {
	t.Setenv(requiredEnvironmentVariable, "development")

	provider := environment.New()
	config, err := provider.Load(context.Background())
	if err != nil {
		t.Fatalf("Load returned unexpected error: %v", err)
	}

	if config.Application.Name != "frappe-api" {
		t.Errorf("Application.Name = %q, want default %q", config.Application.Name, "frappe-api")
	}
	if config.Application.Environment != "development" {
		t.Errorf("Application.Environment = %q, want %q", config.Application.Environment, "development")
	}
	if config.HTTP.Port != 8080 {
		t.Errorf("HTTP.Port = %d, want default 8080", config.HTTP.Port)
	}
	if config.HTTP.ShutdownTimeout != 10*time.Second {
		t.Errorf("HTTP.ShutdownTimeout = %s, want default 10s", config.HTTP.ShutdownTimeout)
	}
	if config.Logging.Level != "info" {
		t.Errorf("Logging.Level = %q, want default %q", config.Logging.Level, "info")
	}
}

func TestLoadReadsEveryVariableFromTheEnvironment(t *testing.T) {
	t.Setenv("APPLICATION_NAME", "custom-name")
	t.Setenv(requiredEnvironmentVariable, "staging")
	t.Setenv("HTTP_PORT", "9090")
	t.Setenv("HTTP_SHUTDOWN_TIMEOUT", "5s")
	t.Setenv("LOGGING_LEVEL", "debug")

	provider := environment.New()
	config, err := provider.Load(context.Background())
	if err != nil {
		t.Fatalf("Load returned unexpected error: %v", err)
	}

	if config.Application.Name != "custom-name" {
		t.Errorf("Application.Name = %q, want %q", config.Application.Name, "custom-name")
	}
	if config.Application.Environment != "staging" {
		t.Errorf("Application.Environment = %q, want %q", config.Application.Environment, "staging")
	}
	if config.HTTP.Port != 9090 {
		t.Errorf("HTTP.Port = %d, want 9090", config.HTTP.Port)
	}
	if config.HTTP.ShutdownTimeout != 5*time.Second {
		t.Errorf("HTTP.ShutdownTimeout = %s, want 5s", config.HTTP.ShutdownTimeout)
	}
	if config.Logging.Level != "debug" {
		t.Errorf("Logging.Level = %q, want %q", config.Logging.Level, "debug")
	}
}

func TestLoadFailsWhenTheRequiredVariableIsMissing(t *testing.T) {
	provider := environment.New()
	_, err := provider.Load(context.Background())
	if err == nil {
		t.Fatal("Load returned nil error when APPLICATION_ENVIRONMENT is unset")
	}
	if !strings.Contains(err.Error(), requiredEnvironmentVariable) {
		t.Fatalf("error does not name %s: %v", requiredEnvironmentVariable, err)
	}
}

func TestLoadFailsOnAnInvalidOneofValue(t *testing.T) {
	t.Setenv(requiredEnvironmentVariable, "not-a-real-environment")

	provider := environment.New()
	_, err := provider.Load(context.Background())
	if err == nil {
		t.Fatal("Load returned nil error for an invalid Environment value")
	}
	if !strings.Contains(err.Error(), requiredEnvironmentVariable) {
		t.Fatalf("error does not name %s: %v", requiredEnvironmentVariable, err)
	}
}

func TestLoadFailsOnAnOutOfRangePort(t *testing.T) {
	t.Setenv(requiredEnvironmentVariable, "development")
	t.Setenv("HTTP_PORT", "99999")

	provider := environment.New()
	_, err := provider.Load(context.Background())
	if err == nil {
		t.Fatal("Load returned nil error for an out-of-range HTTP_PORT")
	}
	if !strings.Contains(err.Error(), "HTTP_PORT") {
		t.Fatalf("error does not name HTTP_PORT: %v", err)
	}
}
