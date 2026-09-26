package query

import (
	"context"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

// GetCity asks for one city by GeoNames id.
type GetCity struct {
	Locale i18n.Locale
	ID     int64
}

// GetCityHandler returns the city, or domain.ErrNotFound.
type GetCityHandler struct {
	cities application.CityReader
}

// NewGetCityHandler returns a GetCityHandler reading
// cities.
func NewGetCityHandler(cities application.CityReader) *GetCityHandler {
	return &GetCityHandler{cities: cities}
}

// Handle returns the city with query.ID.
func (handler *GetCityHandler) Handle(ctx context.Context, query GetCity) (domain.City, error) {
	cities, err := handler.cities.CitiesByID(ctx, query.Locale, []int64{query.ID})
	if err != nil {
		return domain.City{}, err
	}
	for _, city := range cities {
		if city.ID == query.ID {
			return city, nil
		}
	}
	return domain.City{}, domain.ErrNotFound
}
