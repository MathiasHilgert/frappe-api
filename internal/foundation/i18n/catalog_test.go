package i18n_test

import (
	"context"
	"testing"
	"testing/fstest"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
)

func testCatalog(t *testing.T) *i18n.Catalog {
	t.Helper()
	messages := fstest.MapFS{
		"es-419.json": {Data: []byte(`{"greeting": "Hola {{.Name}}", "only_source": "Solo origen", "items": {"one": "{{.Count}} elemento", "other": "{{.Count}} elementos"}}`)},
		"en.json":     {Data: []byte(`{"greeting": "Hello {{.Name}}", "items": {"one": "{{.Count}} item", "other": "{{.Count}} items"}}`)},
		"pt-BR.json":  {Data: []byte(`{"greeting": "Ola {{.Name}}"}`)},
	}
	catalog, err := i18n.NewCatalog(i18n.Settings{
		Source:    i18n.MustParseLocale("es-419"),
		Supported: []i18n.Locale{i18n.MustParseLocale("es-419"), i18n.MustParseLocale("en"), i18n.MustParseLocale("pt-BR")},
		Messages:  messages,
	})
	if err != nil {
		t.Fatalf("NewCatalog returned unexpected error: %v", err)
	}
	return catalog
}

func TestNewCatalogRejectsInvalidSettings(t *testing.T) {
	source := i18n.MustParseLocale("es-419")
	english := i18n.MustParseLocale("en")
	messages := fstest.MapFS{"es-419.json": {Data: []byte(`{"a": "b"}`)}}
	cases := map[string]i18n.Settings{
		"zero source":            {Supported: []i18n.Locale{source}, Messages: messages},
		"source not supported":   {Source: source, Supported: []i18n.Locale{english}, Messages: messages},
		"missing messages":       {Source: source, Supported: []i18n.Locale{source}},
		"locale without file":    {Source: source, Supported: []i18n.Locale{source, english}, Messages: messages},
		"malformed message file": {Source: source, Supported: []i18n.Locale{source}, Messages: fstest.MapFS{"es-419.json": {Data: []byte(`{`)}}},
	}
	for name, settings := range cases {
		if _, err := i18n.NewCatalog(settings); err == nil {
			t.Fatalf("%s: NewCatalog returned nil error", name)
		}
	}
}

func TestNegotiate(t *testing.T) {
	catalog := testCatalog(t)
	cases := map[string]string{
		"":                          "es-419",
		"en-US,en;q=0.9":            "en",
		"pt-BR":                     "pt-BR",
		"pt":                        "pt-BR",
		"es-AR,es;q=0.9":            "es-419",
		"ja":                        "es-419",
		"fr;q=0.9, en;q=0.8":        "en",
		"garbage;;;q=":              "es-419",
		"de, pt-BR;q=0.5, en;q=0.4": "pt-BR",
	}
	for header, want := range cases {
		if got := catalog.Negotiate(header).String(); got != want {
			t.Fatalf("Negotiate(%q) = %q, want %q", header, got, want)
		}
	}
}

func TestTranslateFallsBackToSourceThenKey(t *testing.T) {
	catalog := testCatalog(t)
	portuguese := i18n.MustParseLocale("pt-BR")

	if got := catalog.Translate(portuguese, "greeting", i18n.Data{"Name": "Ana"}); got != "Ola Ana" {
		t.Fatalf("Translate greeting = %q", got)
	}
	if got := catalog.Translate(portuguese, "only_source", nil); got != "Solo origen" {
		t.Fatalf("Translate only_source = %q, want source fallback", got)
	}
	if got := catalog.Translate(portuguese, "unknown.key", nil); got != "unknown.key" {
		t.Fatalf("Translate unknown.key = %q, want the key itself", got)
	}
}

func TestTranslatePluralizesWithCount(t *testing.T) {
	catalog := testCatalog(t)
	english := i18n.MustParseLocale("en")
	if got := catalog.Translate(english, "items", i18n.Data{"Count": 1}); got != "1 item" {
		t.Fatalf("Translate items one = %q", got)
	}
	if got := catalog.Translate(english, "items", i18n.Data{"Count": 3}); got != "3 items" {
		t.Fatalf("Translate items other = %q", got)
	}
}

func TestContextHelpers(t *testing.T) {
	catalog := testCatalog(t)

	if _, ok := i18n.FromContext(context.Background()); ok {
		t.Fatal("FromContext on an empty context reported a locale")
	}
	if got := i18n.T(context.Background(), "greeting"); got != "greeting" {
		t.Fatalf("T without a localized context = %q, want the key", got)
	}

	ctx := catalog.WithLocale(context.Background(), i18n.MustParseLocale("en"))
	locale, ok := i18n.FromContext(ctx)
	if !ok || locale.String() != "en" {
		t.Fatalf("FromContext = %v, %v, want en", locale, ok)
	}
	if got := i18n.TranslateWith(ctx, "greeting", i18n.Data{"Name": "Bo"}); got != "Hello Bo" {
		t.Fatalf("TranslateWith = %q", got)
	}
	if got := i18n.T(ctx, "only_source"); got != "Solo origen" {
		t.Fatalf("T only_source = %q", got)
	}
}
