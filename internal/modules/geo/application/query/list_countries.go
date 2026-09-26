package query

import (
	"context"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

// ListCountries asks for one page of countries ordered by code.
type ListCountries struct {
	Locale i18n.Locale
	Filter application.CountryFilter
}

// ListCountriesHandler returns a page of countries.
type ListCountriesHandler struct {
	countries application.CountryReader
}

// NewListCountriesHandler returns a ListCountriesHandler reading countries.
func NewListCountriesHandler(countries application.CountryReader) *ListCountriesHandler {
	return &ListCountriesHandler{countries: countries}
}

// Handle returns the page query.Filter selects.
func (handler *ListCountriesHandler) Handle(ctx context.Context, query ListCountries) ([]domain.Country, error) {
	return handler.countries.Countries(ctx, query.Locale, query.Filter)
}
