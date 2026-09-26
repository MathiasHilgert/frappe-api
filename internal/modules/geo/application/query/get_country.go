package query

import (
	"context"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

// GetCountry asks for one country by ISO 3166-1 alpha-2 code.
type GetCountry struct {
	Locale i18n.Locale
	Code   string
}

// GetCountryHandler returns the country, or domain.ErrNotFound.
type GetCountryHandler struct {
	countries application.CountryReader
}

// NewGetCountryHandler returns a GetCountryHandler reading countries.
func NewGetCountryHandler(countries application.CountryReader) *GetCountryHandler {
	return &GetCountryHandler{countries: countries}
}

// Handle returns the country with query.Code.
func (handler *GetCountryHandler) Handle(ctx context.Context, query GetCountry) (domain.Country, error) {
	countries, err := handler.countries.CountriesByCode(ctx, query.Locale, []string{query.Code})
	if err != nil {
		return domain.Country{}, err
	}
	for _, country := range countries {
		if country.Code == query.Code {
			return country, nil
		}
	}
	return domain.Country{}, domain.ErrNotFound
}
