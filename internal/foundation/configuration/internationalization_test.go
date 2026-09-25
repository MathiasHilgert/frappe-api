package configuration_test

import (
	"reflect"
	"testing"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
)

func TestValidateAcceptsDefaultInternationalization(t *testing.T) {
	if err := configuration.Validate(validConfiguration()); err != nil {
		t.Fatalf("Validate returned unexpected error: %v", err)
	}
}

func TestValidateRejectsInvalidInternationalization(t *testing.T) {
	cases := map[string]configuration.Internationalization{
		"I18N_SUPPORTED_LOCALES": {SupportedLocales: []string{"es-419", "not a locale"}, SourceLocale: "es-419"},
		"I18N_SOURCE_LOCALE":     {SupportedLocales: []string{"en"}, SourceLocale: "es-419"},
	}
	for variable, settings := range cases {
		loadedConfiguration := validConfiguration()
		loadedConfiguration.Internationalization = settings
		assertViolation(t, loadedConfiguration, variable)
	}

	loadedConfiguration := validConfiguration()
	loadedConfiguration.Internationalization.SupportedLocales = []string{"es-419", "EN", "en"}
	assertViolation(t, loadedConfiguration, "I18N_SUPPORTED_LOCALES")

	loadedConfiguration = validConfiguration()
	loadedConfiguration.Internationalization.SupportedLocales = nil
	assertViolation(t, loadedConfiguration, "I18N_SUPPORTED_LOCALES")

	loadedConfiguration = validConfiguration()
	loadedConfiguration.Internationalization.SourceLocale = "und"
	assertViolation(t, loadedConfiguration, "I18N_SOURCE_LOCALE")
}

func TestValidateRejectsNonCanonicalLocales(t *testing.T) {
	for _, tag := range []string{"iw", "in", "pt-br", "zh-hans"} {
		loadedConfiguration := validConfiguration()
		loadedConfiguration.Internationalization.SupportedLocales = []string{"es-419", tag}
		assertViolation(t, loadedConfiguration, "I18N_SUPPORTED_LOCALES")
	}
}

func TestInternationalizationDefaultsMatchTheExportedConstants(t *testing.T) {
	field, _ := reflect.TypeFor[configuration.Internationalization]().FieldByName("SupportedLocales")
	if got := field.Tag.Get("envDefault"); got != configuration.DefaultSupportedLocales {
		t.Fatalf("SupportedLocales envDefault = %q, want DefaultSupportedLocales %q", got, configuration.DefaultSupportedLocales)
	}
	field, _ = reflect.TypeFor[configuration.Internationalization]().FieldByName("SourceLocale")
	if got := field.Tag.Get("envDefault"); got != configuration.DefaultSourceLocale {
		t.Fatalf("SourceLocale envDefault = %q, want DefaultSourceLocale %q", got, configuration.DefaultSourceLocale)
	}
}
