package dependencies

import (
	"fmt"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n/localizedtext"
	localizedtextpostgres "github.com/MathiasHilgert/frappe-api/internal/foundation/i18n/localizedtext/postgres"
)

// provideInternationalization builds the static catalog and, on it, the
// localized text Service.
func provideInternationalization(settings configuration.Internationalization) (*i18n.Catalog, *localizedtext.Service, error) {
	catalog, err := provideLocalization(settings)
	if err != nil {
		return nil, nil, err
	}
	service, err := provideLocalizedTexts(catalog)
	if err != nil {
		return nil, nil, fmt.Errorf("localized texts: %w", err)
	}
	return catalog, service, nil
}

// provideLocalizedTexts builds the localized text Service (user-entered,
// translatable strings) on the Postgres store, which always joins the
// caller's transaction and so needs no pool of its own. No
// TranslationRequester is wired yet: until machine translation exists,
// nothing is requested or marked pending, and reads fall back to the
// source.
func provideLocalizedTexts(catalog *i18n.Catalog) (*localizedtext.Service, error) {
	return localizedtext.NewService(localizedtext.Settings{
		Store:   localizedtextpostgres.NewStore(),
		Locales: catalog,
	})
}

// provideLocalization builds the static message catalog from the
// embedded catalogs and the I18N_* settings. It fails when a supported
// locale has no embedded catalog, so a misconfigured locale stops the
// application at startup instead of silently serving the source locale.
func provideLocalization(settings configuration.Internationalization) (*i18n.Catalog, error) {
	source, err := i18n.ParseLocale(settings.SourceLocale)
	if err != nil {
		return nil, fmt.Errorf("source locale: %w", err)
	}
	supported, err := i18n.ParseLocales(settings.SupportedLocales)
	if err != nil {
		return nil, fmt.Errorf("supported locales: %w", err)
	}
	return i18n.NewCatalog(i18n.Settings{Source: source, Supported: supported, Messages: i18n.EmbeddedMessages})
}
