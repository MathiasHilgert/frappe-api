package configuration_test

import (
	"testing"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
)

func deepLConfiguration() configuration.Configuration {
	loadedConfiguration := validConfiguration()
	loadedConfiguration.DeepL = configuration.DeepL{
		APIKey:         "key:fx",
		EnglishVariant: "EN-US",
		Timeout:        30 * time.Second,
		BatchSize:      50,
		QuotaPause:     time.Hour,
	}
	return loadedConfiguration
}

func TestValidateAcceptsDeepL(t *testing.T) {
	if err := configuration.Validate(deepLConfiguration()); err != nil {
		t.Fatalf("Validate returned unexpected error: %v", err)
	}
}

func TestValidateIgnoresDeepLSettingsWithoutAKey(t *testing.T) {
	loadedConfiguration := validConfiguration()
	loadedConfiguration.DeepL = configuration.DeepL{EnglishVariant: "EN", BatchSize: -1}
	if err := configuration.Validate(loadedConfiguration); err != nil {
		t.Fatalf("Validate returned unexpected error: %v", err)
	}
}

func TestValidateRejectsInvalidDeepLSettings(t *testing.T) {
	cases := map[string]func(*configuration.Configuration){
		"DEEPL_ENGLISH_VARIANT": func(loaded *configuration.Configuration) { loaded.DeepL.EnglishVariant = "EN" },
		"DEEPL_FORMALITY":       func(loaded *configuration.Configuration) { loaded.DeepL.Formality = "more" },
		"DEEPL_TIMEOUT":         func(loaded *configuration.Configuration) { loaded.DeepL.Timeout = 0 },
		"DEEPL_BATCH_SIZE":      func(loaded *configuration.Configuration) { loaded.DeepL.BatchSize = 51 },
		"DEEPL_BASE_URL":        func(loaded *configuration.Configuration) { loaded.DeepL.BaseURL = "api.deepl.com" },
		"DEEPL_QUOTA_PAUSE":     func(loaded *configuration.Configuration) { loaded.DeepL.QuotaPause = 0 },
	}
	for variable, mutate := range cases {
		t.Run(variable, func(t *testing.T) {
			loadedConfiguration := deepLConfiguration()
			mutate(&loadedConfiguration)
			assertViolation(t, loadedConfiguration, variable)
		})
	}
}

func TestValidateRejectsNegativeLocalizedTextSweepSettings(t *testing.T) {
	cases := map[string]func(*configuration.Configuration){
		"LOCALIZED_TEXTS_EXPIRED_SWEEP_INTERVAL": func(loaded *configuration.Configuration) { loaded.LocalizedTexts.ExpiredSweepInterval = -1 },
		"LOCALIZED_TEXTS_ORPHAN_SWEEP_INTERVAL":  func(loaded *configuration.Configuration) { loaded.LocalizedTexts.OrphanSweepInterval = -1 },
		"LOCALIZED_TEXTS_ORPHAN_MINIMUM_AGE":     func(loaded *configuration.Configuration) { loaded.LocalizedTexts.OrphanMinimumAge = -1 },
		"LOCALIZED_TEXTS_SWEEP_LIMIT":            func(loaded *configuration.Configuration) { loaded.LocalizedTexts.SweepLimit = -1 },
		"LOCALIZED_TEXTS_PENDING_TIMEOUT":        func(loaded *configuration.Configuration) { loaded.LocalizedTexts.PendingTimeout = -1 },
		"LOCALIZED_TEXTS_MAX_REQUEST_ATTEMPTS":   func(loaded *configuration.Configuration) { loaded.LocalizedTexts.MaxRequestAttempts = -1 },
	}
	for variable, mutate := range cases {
		t.Run(variable, func(t *testing.T) {
			loadedConfiguration := validConfiguration()
			mutate(&loadedConfiguration)
			assertViolation(t, loadedConfiguration, variable)
		})
	}
}
