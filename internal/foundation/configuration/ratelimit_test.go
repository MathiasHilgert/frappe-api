package configuration_test

import (
	"strings"
	"testing"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
)

func rateLimitedConfiguration() configuration.Configuration {
	loadedConfiguration := validConfiguration()
	loadedConfiguration.RateLimit = configuration.RateLimit{Enabled: true, Requests: 100, Window: time.Minute, Timeout: 250 * time.Millisecond}
	loadedConfiguration.Valkey = configuration.Valkey{Address: "localhost:6379", DialTimeout: 5 * time.Second, WriteTimeout: 5 * time.Second}
	return loadedConfiguration
}

func assertViolation(t *testing.T, loadedConfiguration configuration.Configuration, variable string) {
	t.Helper()
	err := configuration.Validate(loadedConfiguration)
	if err == nil || !strings.Contains(err.Error(), variable) {
		t.Fatalf("Validate error = %v, want a violation naming %s", err, variable)
	}
}

func TestValidateAcceptsAnEnabledRateLimit(t *testing.T) {
	if err := configuration.Validate(rateLimitedConfiguration()); err != nil {
		t.Fatalf("Validate returned unexpected error: %v", err)
	}
}

func TestValidateAcceptsADisabledRateLimitWithoutValkey(t *testing.T) {
	loadedConfiguration := validConfiguration()
	loadedConfiguration.RateLimit = configuration.RateLimit{Requests: 100, Window: time.Minute, Timeout: time.Second}
	if err := configuration.Validate(loadedConfiguration); err != nil {
		t.Fatalf("Validate returned unexpected error: %v", err)
	}
}

func TestValidateRequiresAValkeyAddressWhenRateLimitIsEnabled(t *testing.T) {
	loadedConfiguration := rateLimitedConfiguration()
	loadedConfiguration.Valkey.Address = ""
	assertViolation(t, loadedConfiguration, "VALKEY_ADDRESS")
}

func TestValidateRejectsInvalidRateLimitSettings(t *testing.T) {
	loadedConfiguration := rateLimitedConfiguration()
	loadedConfiguration.RateLimit.Requests = 0
	assertViolation(t, loadedConfiguration, "RATE_LIMIT_REQUESTS")

	loadedConfiguration = rateLimitedConfiguration()
	loadedConfiguration.RateLimit.Window = 0
	assertViolation(t, loadedConfiguration, "RATE_LIMIT_WINDOW")

	loadedConfiguration = rateLimitedConfiguration()
	loadedConfiguration.Valkey.Database = -1
	assertViolation(t, loadedConfiguration, "VALKEY_DATABASE")
}

func TestValidateRejectsMalformedTrustedProxies(t *testing.T) {
	loadedConfiguration := validConfiguration()
	loadedConfiguration.HTTP.TrustedProxies = []string{"10.0.0.0/8", "not-a-cidr"}
	assertViolation(t, loadedConfiguration, "HTTP_TRUSTED_PROXIES")
}
