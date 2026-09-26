package query

import (
	"context"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

// SearchCities asks for one page of cities whose name matches Text, only
// of CountryCode and SubdivisionID when set.
type SearchCities struct {
	Locale        i18n.Locale
	After         *application.SearchPosition
	SubdivisionID *int64
	Text          string
	CountryCode   string
	Limit         int
}

// SearchCitiesHandler searches cities.
type SearchCitiesHandler struct {
	search placeSearch
}

// NewSearchCitiesHandler returns a SearchCitiesHandler reading through
// readers.
func NewSearchCitiesHandler(readers SearchReaders) *SearchCitiesHandler {
	return &SearchCitiesHandler{search: placeSearch{readers: readers, scope: "cities"}}
}

// Handle returns one page of matches, or ErrQueryTooShort.
func (handler *SearchCitiesHandler) Handle(ctx context.Context, query SearchCities) (SearchPage, error) {
	return handler.search.run(ctx, application.SearchQuery{
		Locale: query.Locale, After: query.After, Text: query.Text, CountryCode: query.CountryCode,
		SubdivisionID: query.SubdivisionID, Limit: query.Limit, Kinds: []domain.PlaceKind{domain.KindCity},
	})
}
