package configuration_test

import (
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
		},
		HTTP: configuration.HTTP{
			Port:            8080,
			ShutdownTimeout: 10 * time.Second,
		},
		Logging: configuration.Logging{
			Level: "info",
		},
	}
}

func TestValidateAcceptsAValidConfiguration(t *testing.T) {
	if err := configuration.Validate(validConfiguration()); err != nil {
		t.Fatalf("Validate returned unexpected error: %v", err)
	}
}

func TestValidateRejectsAMissingApplicationEnvironment(t *testing.T) {
	config := validConfiguration()
	config.Application.Environment = ""

	err := configuration.Validate(config)
	if err == nil {
		t.Fatal("Validate returned nil error for a missing Environment")
	}
	if !strings.Contains(err.Error(), "APPLICATION_ENVIRONMENT") {
		t.Fatalf("error does not name the env var APPLICATION_ENVIRONMENT: %v", err)
	}
}

func TestValidateRejectsAnInvalidApplicationEnvironment(t *testing.T) {
	config := validConfiguration()
	config.Application.Environment = "production-ish"

	err := configuration.Validate(config)
	if err == nil {
		t.Fatal("Validate returned nil error for an invalid Environment")
	}
	if !strings.Contains(err.Error(), "APPLICATION_ENVIRONMENT") {
		t.Fatalf("error does not name the env var APPLICATION_ENVIRONMENT: %v", err)
	}
}

func TestValidateRejectsAnHTTPPortOutOfRange(t *testing.T) {
	config := validConfiguration()
	config.HTTP.Port = 70000

	err := configuration.Validate(config)
	if err == nil {
		t.Fatal("Validate returned nil error for an out-of-range HTTP port")
	}
	if !strings.Contains(err.Error(), "HTTP_PORT") {
		t.Fatalf("error does not name the env var HTTP_PORT: %v", err)
	}
}

func TestValidateRejectsAnInvalidLoggingLevel(t *testing.T) {
	config := validConfiguration()
	config.Logging.Level = "trace"

	err := configuration.Validate(config)
	if err == nil {
		t.Fatal("Validate returned nil error for an invalid Logging level")
	}
	if !strings.Contains(err.Error(), "LOGGING_LEVEL") {
		t.Fatalf("error does not name the env var LOGGING_LEVEL: %v", err)
	}
}

func TestValidateAggregatesMultipleErrors(t *testing.T) {
	config := validConfiguration()
	config.Application.Environment = ""
	config.Logging.Level = "trace"

	err := configuration.Validate(config)
	if err == nil {
		t.Fatal("Validate returned nil error for two invalid fields")
	}
	if !strings.Contains(err.Error(), "APPLICATION_ENVIRONMENT") || !strings.Contains(err.Error(), "LOGGING_LEVEL") {
		t.Fatalf("error does not name both invalid env vars: %v", err)
	}
}
