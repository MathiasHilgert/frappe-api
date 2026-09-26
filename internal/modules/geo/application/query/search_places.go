package query

import (
	"context"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application"
)

// SearchPlaces asks for one page of countries, subdivisions and cities
// whose name matches Text, best match first.
type SearchPlaces struct {
	Locale i18n.Locale
	After  *application.SearchPosition
	// Text is what the user typed, at least MinimumQueryLength characters
	// besides spaces.
	Text string
	// CountryCode keeps only the country itself, its subdivisions and its
	// cities, when set.
	CountryCode string
	Limit       int
}

// SearchPlacesHandler searches every kind of place at once.
type SearchPlacesHandler struct {
	search placeSearch
}

// NewSearchPlacesHandler returns a SearchPlacesHandler reading through
// readers.
func NewSearchPlacesHandler(readers SearchReaders) *SearchPlacesHandler {
	return &SearchPlacesHandler{search: placeSearch{readers: readers, scope: "places"}}
}

// Handle returns one page of matches, or ErrQueryTooShort.
func (handler *SearchPlacesHandler) Handle(ctx context.Context, query SearchPlaces) (SearchPage, error) {
	return handler.search.run(ctx, application.SearchQuery{
		Locale: query.Locale, After: query.After, Text: query.Text, CountryCode: query.CountryCode, Limit: query.Limit,
	})
}
