package environment_test

import (
	"context"
	"errors"
	"strings"
	"testing"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration/environment"
)

// requiredEnvironmentVariable is the one environment variable that has no
// default and must be set for Load to succeed.
const requiredEnvironmentVariable = "APPLICATION_ENVIRONMENT"

// minimalVariables returns the smallest environment map that satisfies
// Load, containing only the required variables.
func minimalVariables() map[string]string {
	return map[string]string{
		requiredEnvironmentVariable: "development",
		"DATABASE_URL":              "postgres://user:password@localhost:5432/frappe",
	}
}

func TestLoadAppliesDefaultsWhenOnlyTheRequiredVariableIsSet(t *testing.T) {
	provider := environment.New(environment.WithVariables(minimalVariables()))
	loadedConfiguration, err := provider.Load(context.Background())
	if err != nil {
		t.Fatalf("Load returned unexpected error: %v", err)
	}

	if loadedConfiguration.Application.Name != "frappe-api" {
		t.Errorf("Application.Name = %q, want default %q", loadedConfiguration.Application.Name, "frappe-api")
	}
	if loadedConfiguration.Application.Environment != "development" {
		t.Errorf("Application.Environment = %q, want %q", loadedConfiguration.Application.Environment, "development")
	}
	if loadedConfiguration.Application.HookTimeout != 30*time.Second {
		t.Errorf("Application.HookTimeout = %s, want default 30s", loadedConfiguration.Application.HookTimeout)
	}
	if loadedConfiguration.HTTP.Port != 8080 {
		t.Errorf("HTTP.Port = %d, want default 8080", loadedConfiguration.HTTP.Port)
	}
	if loadedConfiguration.Logging.Level != "info" {
		t.Errorf("Logging.Level = %q, want default %q", loadedConfiguration.Logging.Level, "info")
	}
	if loadedConfiguration.HTTP.ShutdownDrainDelay != 5*time.Second {
		t.Errorf("HTTP.ShutdownDrainDelay = %s, want default 5s", loadedConfiguration.HTTP.ShutdownDrainDelay)
	}
	if loadedConfiguration.Health.CheckInterval != 10*time.Second {
		t.Errorf("Health.CheckInterval = %s, want default 10s", loadedConfiguration.Health.CheckInterval)
	}
	if loadedConfiguration.Health.CheckTimeout != 2*time.Second {
		t.Errorf("Health.CheckTimeout = %s, want default 2s", loadedConfiguration.Health.CheckTimeout)
	}
	if loadedConfiguration.Health.FailureThreshold != 3 {
		t.Errorf("Health.FailureThreshold = %d, want default 3", loadedConfiguration.Health.FailureThreshold)
	}
	if loadedConfiguration.Database.MaxConnections != 10 {
		t.Errorf("Database.MaxConnections = %d, want default 10", loadedConfiguration.Database.MaxConnections)
	}
	if loadedConfiguration.Database.MinConnections != 2 {
		t.Errorf("Database.MinConnections = %d, want default 2", loadedConfiguration.Database.MinConnections)
	}
	if loadedConfiguration.Database.MaxConnectionLifetime != 30*time.Minute {
		t.Errorf("Database.MaxConnectionLifetime = %s, want default 30m", loadedConfiguration.Database.MaxConnectionLifetime)
	}
	if loadedConfiguration.Database.MaxConnectionIdleTime != 5*time.Minute {
		t.Errorf("Database.MaxConnectionIdleTime = %s, want default 5m", loadedConfiguration.Database.MaxConnectionIdleTime)
	}
	if loadedConfiguration.Database.ConnectTimeout != 5*time.Second {
		t.Errorf("Database.ConnectTimeout = %s, want default 5s", loadedConfiguration.Database.ConnectTimeout)
	}
}

func TestLoadReadsEveryVariableFromTheEnvironment(t *testing.T) {
	provider := environment.New(environment.WithVariables(map[string]string{
		"APPLICATION_NAME":                  "custom-name",
		requiredEnvironmentVariable:         "staging",
		"APPLICATION_HOOK_TIMEOUT":          "10s",
		"HTTP_PORT":                         "9090",
		"HTTP_SHUTDOWN_TIMEOUT":             "5s",
		"HTTP_SHUTDOWN_DRAIN_DELAY":         "1s",
		"LOGGING_LEVEL":                     "debug",
		"HEALTH_CHECK_INTERVAL":             "20s",
		"HEALTH_CHECK_TIMEOUT":              "3s",
		"HEALTH_FAILURE_THRESHOLD":          "5",
		"DATABASE_URL":                      "postgres://user:password@localhost:5432/frappe",
		"DATABASE_MAX_CONNECTIONS":          "20",
		"DATABASE_MIN_CONNECTIONS":          "4",
		"DATABASE_MAX_CONNECTION_LIFETIME":  "1h",
		"DATABASE_MAX_CONNECTION_IDLE_TIME": "10m",
		"DATABASE_CONNECT_TIMEOUT":          "1s",
	}))
	loadedConfiguration, err := provider.Load(context.Background())
	if err != nil {
		t.Fatalf("Load returned unexpected error: %v", err)
	}

	if loadedConfiguration.Application.Name != "custom-name" {
		t.Errorf("Application.Name = %q, want %q", loadedConfiguration.Application.Name, "custom-name")
	}
	if loadedConfiguration.Application.Environment != "staging" {
		t.Errorf("Application.Environment = %q, want %q", loadedConfiguration.Application.Environment, "staging")
	}
	if loadedConfiguration.Application.HookTimeout != 10*time.Second {
		t.Errorf("Application.HookTimeout = %s, want 10s", loadedConfiguration.Application.HookTimeout)
	}
	if loadedConfiguration.HTTP.Port != 9090 {
		t.Errorf("HTTP.Port = %d, want 9090", loadedConfiguration.HTTP.Port)
	}
	if loadedConfiguration.Logging.Level != "debug" {
		t.Errorf("Logging.Level = %q, want %q", loadedConfiguration.Logging.Level, "debug")
	}
	if loadedConfiguration.HTTP.ShutdownDrainDelay != time.Second {
		t.Errorf("HTTP.ShutdownDrainDelay = %s, want 1s", loadedConfiguration.HTTP.ShutdownDrainDelay)
	}
	if loadedConfiguration.Health.CheckInterval != 20*time.Second {
		t.Errorf("Health.CheckInterval = %s, want 20s", loadedConfiguration.Health.CheckInterval)
	}
	if loadedConfiguration.Health.CheckTimeout != 3*time.Second {
		t.Errorf("Health.CheckTimeout = %s, want 3s", loadedConfiguration.Health.CheckTimeout)
	}
	if loadedConfiguration.Health.FailureThreshold != 5 {
		t.Errorf("Health.FailureThreshold = %d, want 5", loadedConfiguration.Health.FailureThreshold)
	}
	if loadedConfiguration.Database.URL != "postgres://user:password@localhost:5432/frappe" {
		t.Errorf("Database.URL = %q, want the configured URL", loadedConfiguration.Database.URL)
	}
	if loadedConfiguration.Database.MaxConnections != 20 {
		t.Errorf("Database.MaxConnections = %d, want 20", loadedConfiguration.Database.MaxConnections)
	}
	if loadedConfiguration.Database.MinConnections != 4 {
		t.Errorf("Database.MinConnections = %d, want 4", loadedConfiguration.Database.MinConnections)
	}
	if loadedConfiguration.Database.MaxConnectionLifetime != time.Hour {
		t.Errorf("Database.MaxConnectionLifetime = %s, want 1h", loadedConfiguration.Database.MaxConnectionLifetime)
	}
	if loadedConfiguration.Database.MaxConnectionIdleTime != 10*time.Minute {
		t.Errorf("Database.MaxConnectionIdleTime = %s, want 10m", loadedConfiguration.Database.MaxConnectionIdleTime)
	}
	if loadedConfiguration.Database.ConnectTimeout != time.Second {
		t.Errorf("Database.ConnectTimeout = %s, want 1s", loadedConfiguration.Database.ConnectTimeout)
	}
}

func TestLoadFailsWhenTheRequiredVariableIsMissing(t *testing.T) {
	provider := environment.New(environment.WithVariables(map[string]string{}))
	_, err := provider.Load(context.Background())
	if err == nil {
		t.Fatal("Load returned nil error when APPLICATION_ENVIRONMENT is unset")
	}
	if !strings.Contains(err.Error(), requiredEnvironmentVariable) {
		t.Fatalf("error does not name %s: %v", requiredEnvironmentVariable, err)
	}
}

func TestLoadFailsOnAnInvalidOneofValue(t *testing.T) {
	provider := environment.New(environment.WithVariables(map[string]string{
		requiredEnvironmentVariable: "not-a-real-environment",
	}))
	_, err := provider.Load(context.Background())
	if err == nil {
		t.Fatal("Load returned nil error for an invalid Environment value")
	}
	if !strings.Contains(err.Error(), requiredEnvironmentVariable) {
		t.Fatalf("error does not name %s: %v", requiredEnvironmentVariable, err)
	}
}

func TestLoadFailsOnAnOutOfRangePort(t *testing.T) {
	provider := environment.New(environment.WithVariables(map[string]string{
		requiredEnvironmentVariable: "development",
		"HTTP_PORT":                 "99999",
	}))
	_, err := provider.Load(context.Background())
	if err == nil {
		t.Fatal("Load returned nil error for an out-of-range HTTP_PORT")
	}
	if !strings.Contains(err.Error(), "HTTP_PORT") {
		t.Fatalf("error does not name HTTP_PORT: %v", err)
	}
}

func TestLoadFailsOnANonNumericPort(t *testing.T) {
	provider := environment.New(environment.WithVariables(map[string]string{
		requiredEnvironmentVariable: "development",
		"HTTP_PORT":                 "not-a-number",
	}))
	_, err := provider.Load(context.Background())
	if err == nil {
		t.Fatal("Load returned nil error for a non-numeric HTTP_PORT")
	}
	// This fails during env parsing, before configuration.Validate runs,
	// so the underlying caarlos0/env error names the Go struct field
	// ("Port") rather than the environment variable name.
	if !strings.Contains(err.Error(), "Port") {
		t.Fatalf("error does not name the Port field: %v", err)
	}
}

func TestLoadFailsWhenPortIsZero(t *testing.T) {
	provider := environment.New(environment.WithVariables(map[string]string{
		requiredEnvironmentVariable: "development",
		"HTTP_PORT":                 "0",
	}))
	_, err := provider.Load(context.Background())
	if err == nil {
		t.Fatal("Load returned nil error for HTTP_PORT=0")
	}
	if !strings.Contains(err.Error(), "HTTP_PORT") {
		t.Fatalf("error does not name HTTP_PORT: %v", err)
	}
}

func TestLoadFallsBackToTheDefaultWhenApplicationNameIsExplicitlyEmpty(t *testing.T) {
	// caarlos0/env treats an explicitly empty value the same as an unset
	// one whenever envDefault is declared, so this falls back to the
	// default instead of failing; this test pins that documented
	// behavior rather than the required-string validation, which never
	// runs because the empty value never reaches Configuration.
	provider := environment.New(environment.WithVariables(map[string]string{
		requiredEnvironmentVariable: "development",
		"APPLICATION_NAME":          "",
		"DATABASE_URL":              "postgres://user:password@localhost:5432/frappe",
	}))
	loadedConfiguration, err := provider.Load(context.Background())
	if err != nil {
		t.Fatalf("Load returned unexpected error for an explicitly empty APPLICATION_NAME: %v", err)
	}
	if loadedConfiguration.Application.Name != "frappe-api" {
		t.Errorf("Application.Name = %q, want default %q", loadedConfiguration.Application.Name, "frappe-api")
	}
}

func TestLoadWrapsAValidationErrorRetrievableWithErrorsAs(t *testing.T) {
	provider := environment.New(environment.WithVariables(map[string]string{}))
	_, err := provider.Load(context.Background())

	var validationError *configuration.ValidationError
	if !errors.As(err, &validationError) {
		t.Fatalf("errors.As could not extract a *configuration.ValidationError from: %v", err)
	}
}

func TestLoadDefaultsToTheProcessEnvironmentWhenNoVariablesAreInjected(t *testing.T) {
	t.Setenv(requiredEnvironmentVariable, "development")
	t.Setenv("DATABASE_URL", "postgres://user:password@localhost:5432/frappe")

	provider := environment.New()
	loadedConfiguration, err := provider.Load(context.Background())
	if err != nil {
		t.Fatalf("Load returned unexpected error: %v", err)
	}
	if loadedConfiguration.Application.Environment != "development" {
		t.Errorf("Application.Environment = %q, want %q", loadedConfiguration.Application.Environment, "development")
	}
}
