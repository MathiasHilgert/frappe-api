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
// Load, containing only the required variable.
func minimalVariables() map[string]string {
	return map[string]string{
		requiredEnvironmentVariable: "development",
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
}

func TestLoadReadsEveryVariableFromTheEnvironment(t *testing.T) {
	provider := environment.New(environment.WithVariables(map[string]string{
		"APPLICATION_NAME":          "custom-name",
		requiredEnvironmentVariable: "staging",
		"APPLICATION_HOOK_TIMEOUT":  "5s",
		"HTTP_PORT":                 "9090",
		"LOGGING_LEVEL":             "debug",
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
	if loadedConfiguration.Application.HookTimeout != 5*time.Second {
		t.Errorf("Application.HookTimeout = %s, want 5s", loadedConfiguration.Application.HookTimeout)
	}
	if loadedConfiguration.HTTP.Port != 9090 {
		t.Errorf("HTTP.Port = %d, want 9090", loadedConfiguration.HTTP.Port)
	}
	if loadedConfiguration.Logging.Level != "debug" {
		t.Errorf("Logging.Level = %q, want %q", loadedConfiguration.Logging.Level, "debug")
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

	provider := environment.New()
	loadedConfiguration, err := provider.Load(context.Background())
	if err != nil {
		t.Fatalf("Load returned unexpected error: %v", err)
	}
	if loadedConfiguration.Application.Environment != "development" {
		t.Errorf("Application.Environment = %q, want %q", loadedConfiguration.Application.Environment, "development")
	}
}
