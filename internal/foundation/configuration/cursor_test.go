package configuration_test

import (
	"strings"
	"testing"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
)

const validCursorSecret = "0123456789abcdef0123456789abcdef"

func TestValidateAllowsAnEmptyCursorSecretInDevelopment(t *testing.T) {
	if err := configuration.Validate(validConfiguration()); err != nil {
		t.Fatalf("Validate returned unexpected error: %v", err)
	}
}

func TestValidateRequiresACursorSecretOutsideDevelopment(t *testing.T) {
	for _, environment := range []string{"staging", "production"} {
		loadedConfiguration := validConfiguration()
		loadedConfiguration.Application.Environment = environment

		err := configuration.Validate(loadedConfiguration)
		if err == nil || !strings.Contains(err.Error(), "HTTP_CURSOR_SECRET") {
			t.Fatalf("%s: Validate error = %v, want a violation naming HTTP_CURSOR_SECRET", environment, err)
		}

		loadedConfiguration.HTTP.CursorSecret = validCursorSecret
		if err := configuration.Validate(loadedConfiguration); err != nil {
			t.Fatalf("%s: Validate returned unexpected error: %v", environment, err)
		}
	}
}

func TestValidateRejectsAShortCursorSecret(t *testing.T) {
	loadedConfiguration := validConfiguration()
	loadedConfiguration.HTTP.CursorSecret = "short"

	err := configuration.Validate(loadedConfiguration)
	if err == nil || !strings.Contains(err.Error(), "HTTP_CURSOR_SECRET") || strings.Contains(err.Error(), "short") {
		t.Fatalf("Validate error = %v, want a violation naming HTTP_CURSOR_SECRET without its value", err)
	}
}

func TestValidateRejectsAShortPreviousCursorSecret(t *testing.T) {
	loadedConfiguration := validConfiguration()
	loadedConfiguration.HTTP.CursorSecret = validCursorSecret
	loadedConfiguration.HTTP.CursorPreviousSecrets = []string{validCursorSecret, "short"}

	err := configuration.Validate(loadedConfiguration)
	if err == nil || !strings.Contains(err.Error(), "HTTP_CURSOR_PREVIOUS_SECRETS") {
		t.Fatalf("Validate error = %v, want a violation naming HTTP_CURSOR_PREVIOUS_SECRETS", err)
	}
}

func TestValidateRejectsPreviousCursorSecretsWithoutACurrentOne(t *testing.T) {
	loadedConfiguration := validConfiguration()
	loadedConfiguration.HTTP.CursorPreviousSecrets = []string{validCursorSecret}

	err := configuration.Validate(loadedConfiguration)
	if err == nil || !strings.Contains(err.Error(), "HTTP_CURSOR_PREVIOUS_SECRETS") {
		t.Fatalf("Validate error = %v, want a violation naming HTTP_CURSOR_PREVIOUS_SECRETS", err)
	}
}
