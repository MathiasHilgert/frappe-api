package dependencies

import (
	"fmt"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
)

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
