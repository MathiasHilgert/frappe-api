package query

import (
	"context"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

// SearchCountries asks for one page of countries whose name matches Text.
type SearchCountries struct {
	Locale i18n.Locale
	After  *application.SearchPosition
	Text   string
	Limit  int
}

// SearchCountriesHandler searches countries.
type SearchCountriesHandler struct {
	search placeSearch
}

// NewSearchCountriesHandler returns a SearchCountriesHandler reading
// through readers.
func NewSearchCountriesHandler(readers SearchReaders) *SearchCountriesHandler {
	return &SearchCountriesHandler{search: placeSearch{readers: readers, scope: "countries"}}
}

// Handle returns one page of matches, or ErrQueryTooShort.
func (handler *SearchCountriesHandler) Handle(ctx context.Context, query SearchCountries) (SearchPage, error) {
	return handler.search.run(ctx, application.SearchQuery{
		Locale: query.Locale, After: query.After, Text: query.Text, Limit: query.Limit,
		Kinds: []domain.PlaceKind{domain.KindCountry},
	})
}
