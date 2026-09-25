package configuration_test

import (
	"strings"
	"testing"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
)

func TestValidateAcceptsValidCORSOrigins(t *testing.T) {
	loadedConfiguration := validConfiguration()
	loadedConfiguration.HTTP.CORSAllowedOrigins = []string{"https://app.example.com", "http://localhost:3000"}
	loadedConfiguration.HTTP.CORSAllowCredentials = true

	if err := configuration.Validate(loadedConfiguration); err != nil {
		t.Fatalf("Validate returned unexpected error: %v", err)
	}
}

func TestValidateAcceptsAWildcardOriginWithoutCredentials(t *testing.T) {
	loadedConfiguration := validConfiguration()
	loadedConfiguration.HTTP.CORSAllowedOrigins = []string{"*"}

	if err := configuration.Validate(loadedConfiguration); err != nil {
		t.Fatalf("Validate returned unexpected error: %v", err)
	}
}

func TestValidateRejectsAWildcardOriginWithCredentials(t *testing.T) {
	loadedConfiguration := validConfiguration()
	loadedConfiguration.HTTP.CORSAllowedOrigins = []string{"*"}
	loadedConfiguration.HTTP.CORSAllowCredentials = true

	err := configuration.Validate(loadedConfiguration)
	if err == nil || !strings.Contains(err.Error(), "HTTP_CORS_ALLOW_CREDENTIALS") {
		t.Fatalf("Validate error = %v, want a violation naming HTTP_CORS_ALLOW_CREDENTIALS", err)
	}
}

func TestValidateRejectsMalformedCORSOrigins(t *testing.T) {
	for _, origin := range []string{
		"app.example.com",
		"ftp://app.example.com",
		"https://app.example.com/path",
		"https://app.example.com?query=1",
		"https://user@app.example.com",
		"https://",
		"",
	} {
		loadedConfiguration := validConfiguration()
		loadedConfiguration.HTTP.CORSAllowedOrigins = []string{origin}

		err := configuration.Validate(loadedConfiguration)
		if err == nil || !strings.Contains(err.Error(), "HTTP_CORS_ALLOWED_ORIGINS") {
			t.Fatalf("origin %q: Validate error = %v, want a violation naming HTTP_CORS_ALLOWED_ORIGINS", origin, err)
		}
	}
}

func TestValidateRejectsANegativeCORSMaxAge(t *testing.T) {
	loadedConfiguration := validConfiguration()
	loadedConfiguration.HTTP.CORSMaxAge = -1

	err := configuration.Validate(loadedConfiguration)
	if err == nil || !strings.Contains(err.Error(), "HTTP_CORS_MAX_AGE") {
		t.Fatalf("Validate error = %v, want a violation naming HTTP_CORS_MAX_AGE", err)
	}
}
