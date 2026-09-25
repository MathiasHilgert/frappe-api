package configuration_test

import (
	"errors"
	"strings"
	"testing"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
)

// validConfiguration returns a Configuration that satisfies every
// validation rule, so individual tests can mutate one field at a time.
func validConfiguration() configuration.Configuration {
	return configuration.Configuration{
		Application: configuration.Application{
			Name:        "frappe-api",
			Environment: "development",
			HookTimeout: 30 * time.Second,
		},
		HTTP: configuration.HTTP{
			Port:                 8080,
			ShutdownTimeout:      10 * time.Second,
			ReadHeaderTimeout:    5 * time.Second,
			ReadTimeout:          10 * time.Second,
			WriteTimeout:         10 * time.Second,
			IdleTimeout:          60 * time.Second,
			MaxHeaderBytes:       1048576,
			MaxBodyBytes:         2097152,
			DocumentationEnabled: true,
			ShutdownDrainDelay:   5 * time.Second,
		},
		Logging: configuration.Logging{
			Level: "info",
		},
		Health: configuration.Health{
			CheckInterval:    10 * time.Second,
			CheckTimeout:     2 * time.Second,
			FailureThreshold: 3,
		},
		Database: configuration.Database{
			URL:                   "postgres://user:password@localhost:5432/frappe",
			MaxConnections:        10,
			MinConnections:        2,
			MaxConnectionLifetime: 30 * time.Minute,
			MaxConnectionIdleTime: 5 * time.Minute,
			ConnectTimeout:        5 * time.Second,
		},
	}
}

func TestValidateAcceptsAValidConfiguration(t *testing.T) {
	if err := configuration.Validate(validConfiguration()); err != nil {
		t.Fatalf("Validate returned unexpected error: %v", err)
	}
}

func TestValidateRejectsAMissingApplicationEnvironment(t *testing.T) {
	loadedConfiguration := validConfiguration()
	loadedConfiguration.Application.Environment = ""

	err := configuration.Validate(loadedConfiguration)
	if err == nil {
		t.Fatal("Validate returned nil error for a missing Environment")
	}
	if !strings.Contains(err.Error(), "APPLICATION_ENVIRONMENT") {
		t.Fatalf("error does not name the env var APPLICATION_ENVIRONMENT: %v", err)
	}
}

func TestValidateRejectsAnInvalidApplicationEnvironment(t *testing.T) {
	loadedConfiguration := validConfiguration()
	loadedConfiguration.Application.Environment = "production-ish"

	err := configuration.Validate(loadedConfiguration)
	if err == nil {
		t.Fatal("Validate returned nil error for an invalid Environment")
	}
	if !strings.Contains(err.Error(), "APPLICATION_ENVIRONMENT") {
		t.Fatalf("error does not name the env var APPLICATION_ENVIRONMENT: %v", err)
	}
}

func TestValidateRejectsAnHTTPPortOutOfRange(t *testing.T) {
	loadedConfiguration := validConfiguration()
	loadedConfiguration.HTTP.Port = 70000

	err := configuration.Validate(loadedConfiguration)
	if err == nil {
		t.Fatal("Validate returned nil error for an out-of-range HTTP port")
	}
	if !strings.Contains(err.Error(), "HTTP_PORT") {
		t.Fatalf("error does not name the env var HTTP_PORT: %v", err)
	}
}

func TestValidateRejectsAnInvalidLoggingLevel(t *testing.T) {
	loadedConfiguration := validConfiguration()
	loadedConfiguration.Logging.Level = "trace"

	err := configuration.Validate(loadedConfiguration)
	if err == nil {
		t.Fatal("Validate returned nil error for an invalid Logging level")
	}
	if !strings.Contains(err.Error(), "LOGGING_LEVEL") {
		t.Fatalf("error does not name the env var LOGGING_LEVEL: %v", err)
	}
}

func TestValidateAggregatesMultipleErrors(t *testing.T) {
	loadedConfiguration := validConfiguration()
	loadedConfiguration.Application.Environment = ""
	loadedConfiguration.Logging.Level = "trace"

	err := configuration.Validate(loadedConfiguration)
	if err == nil {
		t.Fatal("Validate returned nil error for two invalid fields")
	}
	if !strings.Contains(err.Error(), "APPLICATION_ENVIRONMENT") || !strings.Contains(err.Error(), "LOGGING_LEVEL") {
		t.Fatalf("error does not name both invalid env vars: %v", err)
	}
}

func TestValidateRejectsAHookTimeoutShorterThanDrainPlusShutdown(t *testing.T) {
	loadedConfiguration := validConfiguration()
	loadedConfiguration.HTTP.ShutdownDrainDelay = 5 * time.Second
	loadedConfiguration.HTTP.ShutdownTimeout = 10 * time.Second
	loadedConfiguration.Application.HookTimeout = 10 * time.Second

	err := configuration.Validate(loadedConfiguration)
	if err == nil {
		t.Fatal("Validate returned nil error for a HookTimeout shorter than ShutdownDrainDelay+ShutdownTimeout")
	}
	if !strings.Contains(err.Error(), "APPLICATION_HOOK_TIMEOUT") {
		t.Fatalf("error does not name APPLICATION_HOOK_TIMEOUT: %v", err)
	}
}

func TestValidateAcceptsAHookTimeoutEqualToDrainPlusShutdown(t *testing.T) {
	loadedConfiguration := validConfiguration()
	loadedConfiguration.HTTP.ShutdownDrainDelay = 5 * time.Second
	loadedConfiguration.HTTP.ShutdownTimeout = 10 * time.Second
	loadedConfiguration.Application.HookTimeout = 15 * time.Second

	if err := configuration.Validate(loadedConfiguration); err != nil {
		t.Fatalf("Validate returned unexpected error: %v", err)
	}
}

func TestValidateRejectsANegativeApplicationHookTimeout(t *testing.T) {
	loadedConfiguration := validConfiguration()
	loadedConfiguration.Application.HookTimeout = -1 * time.Second

	err := configuration.Validate(loadedConfiguration)
	if err == nil {
		t.Fatal("Validate returned nil error for a negative HookTimeout")
	}
	if !strings.Contains(err.Error(), "APPLICATION_HOOK_TIMEOUT") {
		t.Fatalf("error does not name the env var APPLICATION_HOOK_TIMEOUT: %v", err)
	}
}

func TestValidateRejectsAZeroApplicationHookTimeout(t *testing.T) {
	loadedConfiguration := validConfiguration()
	loadedConfiguration.Application.HookTimeout = 0

	err := configuration.Validate(loadedConfiguration)
	if err == nil {
		t.Fatal("Validate returned nil error for a zero HookTimeout")
	}
	if !strings.Contains(err.Error(), "APPLICATION_HOOK_TIMEOUT") {
		t.Fatalf("error does not name the env var APPLICATION_HOOK_TIMEOUT: %v", err)
	}
}

func TestValidateErrorNeverIncludesTheFailingValue(t *testing.T) {
	const sensitiveValue = "not-a-real-environment-value"

	loadedConfiguration := validConfiguration()
	loadedConfiguration.Application.Environment = sensitiveValue

	err := configuration.Validate(loadedConfiguration)
	if err == nil {
		t.Fatal("Validate returned nil error for an invalid Environment")
	}
	if strings.Contains(err.Error(), sensitiveValue) {
		t.Fatalf("error exposes the failing value: %v", err)
	}
}

func TestValidateReturnsAValidationErrorUsableWithErrorsAs(t *testing.T) {
	loadedConfiguration := validConfiguration()
	loadedConfiguration.Application.Environment = ""

	err := configuration.Validate(loadedConfiguration)

	var validationError *configuration.ValidationError
	if !errors.As(err, &validationError) {
		t.Fatalf("errors.As could not extract a *ValidationError from: %v", err)
	}
	if len(validationError.Violations) != 1 {
		t.Fatalf("Violations = %d entries, want 1: %+v", len(validationError.Violations), validationError.Violations)
	}
	if validationError.Violations[0].Variable != "APPLICATION_ENVIRONMENT" {
		t.Fatalf("Violations[0].Variable = %q, want %q", validationError.Violations[0].Variable, "APPLICATION_ENVIRONMENT")
	}
	if validationError.Violations[0].Rule != "required" {
		t.Fatalf("Violations[0].Rule = %q, want %q", validationError.Violations[0].Rule, "required")
	}
}

// pointerNestedTestConfiguration exercises resolveVariableName against a
// pointer struct field, a prefix-less nested struct, and a slice of
// structs, none of which the real Configuration currently declares.
type pointerNestedTestConfiguration struct {
	Nested  *nestedTestStruct `envPrefix:"NESTED_"`
	Bare    bareTestStruct
	Servers []serverTestStruct `envPrefix:"SERVERS_" validate:"required,dive"`
}

type nestedTestStruct struct {
	Value string `env:"VALUE" validate:"required"`
}

type bareTestStruct struct {
	Value string `env:"BARE_VALUE" validate:"required"`
}

type serverTestStruct struct {
	Host string `env:"HOST" validate:"required"`
}

func TestValidateHandlesPointerPrefixlessAndSliceShapes(t *testing.T) {
	value := pointerNestedTestConfiguration{
		Nested:  &nestedTestStruct{Value: ""},
		Bare:    bareTestStruct{Value: ""},
		Servers: []serverTestStruct{{Host: ""}},
	}

	validationError := configuration.ValidateStruct(value)
	if validationError == nil {
		t.Fatal("ValidateStruct returned nil error for two invalid fields")
	}

	variables := make(map[string]string, len(validationError.Violations))
	for _, violation := range validationError.Violations {
		variables[violation.Variable] = violation.Rule
	}

	if _, ok := variables["NESTED_VALUE"]; !ok {
		t.Fatalf("violations do not resolve the pointer nested field to NESTED_VALUE: %+v", validationError.Violations)
	}
	if _, ok := variables["BARE_VALUE"]; !ok {
		t.Fatalf("violations do not resolve the prefix-less nested field to BARE_VALUE: %+v", validationError.Violations)
	}
	if _, ok := variables["SERVERS_HOST"]; !ok {
		t.Fatalf("violations do not resolve the slice element field to SERVERS_HOST: %+v", validationError.Violations)
	}
}
