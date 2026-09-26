package query

import (
	"context"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

// FindCountries asks for the countries with Codes, for example the
// countries a page of cities expands.
type FindCountries struct {
	Locale i18n.Locale
	Codes  []string
}

// FindCountriesHandler returns the countries that exist, by code, with
// one read for the whole batch (none for no codes).
type FindCountriesHandler struct {
	countries application.CountryReader
}

// NewFindCountriesHandler returns a FindCountriesHandler reading countries.
func NewFindCountriesHandler(countries application.CountryReader) *FindCountriesHandler {
	return &FindCountriesHandler{countries: countries}
}

// Handle returns the found countries by code.
func (handler *FindCountriesHandler) Handle(ctx context.Context, query FindCountries) (map[string]domain.Country, error) {
	result := map[string]domain.Country{}
	codes := distinct[string](query.Codes).values()
	if len(codes) == 0 {
		return result, nil
	}
	countries, err := handler.countries.CountriesByCode(ctx, query.Locale, codes)
	if err != nil {
		return nil, err
	}
	for _, country := range countries {
		result[country.Code] = country
	}
	return result, nil
}
