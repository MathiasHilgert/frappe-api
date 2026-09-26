package query

import (
	"context"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

// SearchSubdivisions asks for one page of subdivisions whose name matches
// Text, only of CountryCode when set.
type SearchSubdivisions struct {
	Locale      i18n.Locale
	After       *application.SearchPosition
	Text        string
	CountryCode string
	Limit       int
}

// SearchSubdivisionsHandler searches subdivisions.
type SearchSubdivisionsHandler struct {
	search placeSearch
}

// NewSearchSubdivisionsHandler returns a SearchSubdivisionsHandler reading
// through readers.
func NewSearchSubdivisionsHandler(readers SearchReaders) *SearchSubdivisionsHandler {
	return &SearchSubdivisionsHandler{search: placeSearch{readers: readers, scope: "subdivisions"}}
}

// Handle returns one page of matches, or ErrQueryTooShort.
func (handler *SearchSubdivisionsHandler) Handle(ctx context.Context, query SearchSubdivisions) (SearchPage, error) {
	return handler.search.run(ctx, application.SearchQuery{
		Locale: query.Locale, After: query.After, Text: query.Text, CountryCode: query.CountryCode, Limit: query.Limit,
		Kinds: []domain.PlaceKind{domain.KindSubdivision},
	})
}
