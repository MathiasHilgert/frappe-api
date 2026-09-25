package dependencies

import (
	"context"
	"fmt"

	"github.com/jackc/pgx/v5/pgxpool"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/application"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n/localizedtext"
	localizedtextpostgres "github.com/MathiasHilgert/frappe-api/internal/foundation/i18n/localizedtext/postgres"
)

// localeCheckDependencyName names the startup check that every supported
// locale exists in the locales table.
const localeCheckDependencyName = "localized-text-locales"

// provideLocaleCheck registers a startup step, after the database pool,
// that fails fast when a configured locale (the source is one of the
// supported) is missing from the locales table, instead of failing
// every localized text write in it at runtime.
func provideLocaleCheck(instance *application.Application, pool *application.Handle[*pgxpool.Pool], catalog *i18n.Catalog) {
	application.Provide(instance, application.Dependency[struct{}]{
		Name: localeCheckDependencyName,
		Up: func(ctx context.Context) (struct{}, error) {
			return checkLocales(pool, catalog)(ctx)
		},
	})
}

// checkLocales returns the Up of the locale check.
func checkLocales(pool *application.Handle[*pgxpool.Pool], catalog *i18n.Catalog) func(ctx context.Context) (struct{}, error) {
	return func(ctx context.Context) (struct{}, error) {
		connected, ready := pool.Get()
		if !ready || connected == nil {
			return struct{}{}, errDatabaseNotReady
		}
		return struct{}{}, localizedtextpostgres.CheckLocales(ctx, connected, catalog.Supported())
	}
}

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
