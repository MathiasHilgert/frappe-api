package i18n_test

import (
	"io/fs"
	"slices"
	"strings"
	"testing"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
)

// defaultLocales is the I18N_SUPPORTED_LOCALES default. Every one of them
// must ship an embedded catalog.
var defaultLocales = strings.Split(configuration.DefaultSupportedLocales, ",")

func TestEmbeddedCatalogsHaveEveryPluralForm(t *testing.T) {
	problems, err := i18n.CheckPluralForms(i18n.EmbeddedMessages, configuration.DefaultSourceLocale, defaultLocales)
	if err != nil {
		t.Fatal(err)
	}
	for _, problem := range problems {
		t.Error(problem)
	}
}

// TestEmbeddedCatalogsAreComplete fails when any embedded catalog is
// missing a key present in the source locale (es-419), has a key the
// source does not, or has an empty translation, so an incomplete
// translation never reaches production.
func TestEmbeddedCatalogsAreComplete(t *testing.T) {
	source, err := i18n.MessageKeys(i18n.EmbeddedMessages, "es-419")
	if err != nil {
		t.Fatalf("reading the source catalog: %v", err)
	}
	if len(source) == 0 {
		t.Fatal("the source catalog has no keys")
	}
	for _, name := range defaultLocales {
		keys, err := i18n.MessageKeys(i18n.EmbeddedMessages, name)
		if err != nil {
			t.Fatalf("%s: %v", name, err)
		}
		for key := range source {
			if strings.TrimSpace(keys[key]) == "" {
				t.Errorf("%s: missing or empty key %q", name, key)
			}
		}
		for key := range keys {
			if _, ok := source[key]; !ok {
				t.Errorf("%s: key %q is not in the source locale es-419", name, key)
			}
		}
	}
}

func TestEmbeddedCatalogsMatchDefaultLocales(t *testing.T) {
	files, err := fs.Glob(i18n.EmbeddedMessages, "*.json")
	if err != nil {
		t.Fatal(err)
	}
	for _, file := range files {
		if !slices.Contains(defaultLocales, strings.TrimSuffix(file, ".json")) {
			t.Errorf("embedded catalog %s is not a default supported locale", file)
		}
	}
	locales, err := i18n.ParseLocales(defaultLocales)
	if err != nil {
		t.Fatal(err)
	}
	if _, err := i18n.NewCatalog(i18n.Settings{Source: locales[0], Supported: locales, Messages: i18n.EmbeddedMessages}); err != nil {
		t.Fatalf("NewCatalog over the embedded catalogs: %v", err)
	}
}
