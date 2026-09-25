package i18n_test

import (
	"testing"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
)

func TestParseLocaleCanonicalizesBCP47(t *testing.T) {
	cases := map[string]string{
		"es-419":  "es-419",
		"pt-br":   "pt-BR",
		"zh-hans": "zh-Hans",
		"EN":      "en",
	}
	for input, want := range cases {
		locale, err := i18n.ParseLocale(input)
		if err != nil {
			t.Fatalf("ParseLocale(%q) returned unexpected error: %v", input, err)
		}
		if got := locale.String(); got != want {
			t.Fatalf("ParseLocale(%q) = %q, want %q", input, got, want)
		}
	}
}

func TestParseLocaleRejectsInvalidOrUndetermined(t *testing.T) {
	for _, input := range []string{"", "und", "not a locale", "x"} {
		if _, err := i18n.ParseLocale(input); err == nil {
			t.Fatalf("ParseLocale(%q) returned nil error, want an error", input)
		}
	}
}

func TestParseLocalesRejectsDuplicates(t *testing.T) {
	if _, err := i18n.ParseLocales([]string{"en", "EN"}); err == nil {
		t.Fatal("ParseLocales with a duplicate returned nil error")
	}
	locales, err := i18n.ParseLocales([]string{"es-419", "en"})
	if err != nil || len(locales) != 2 {
		t.Fatalf("ParseLocales = %v, %v, want two locales", locales, err)
	}
}
