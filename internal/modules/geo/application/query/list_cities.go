package query

import (
	"context"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

// ListCities asks for one page of cities ordered by id.
type ListCities struct {
	Locale i18n.Locale
	Filter application.CityFilter
}

// ListCitiesHandler returns a page of cities.
type ListCitiesHandler struct {
	cities application.CityReader
}

// NewListCitiesHandler returns a ListCitiesHandler reading
// cities.
func NewListCitiesHandler(cities application.CityReader) *ListCitiesHandler {
	return &ListCitiesHandler{cities: cities}
}

// Handle returns the page query.Filter selects.
func (handler *ListCitiesHandler) Handle(ctx context.Context, query ListCities) ([]domain.City, error) {
	return handler.cities.Cities(ctx, query.Locale, query.Filter)
}
