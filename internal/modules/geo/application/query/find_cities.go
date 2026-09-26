package query

import (
	"context"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

// FindCities asks for the cities with IDs, for example the
// cities a page of cities expands.
type FindCities struct {
	Locale i18n.Locale
	IDs    []int64
}

// FindCitiesHandler returns the cities that exist, by id, with
// one read for the whole batch (none for no ids).
type FindCitiesHandler struct {
	cities application.CityReader
}

// NewFindCitiesHandler returns a FindCitiesHandler reading
// cities.
func NewFindCitiesHandler(cities application.CityReader) *FindCitiesHandler {
	return &FindCitiesHandler{cities: cities}
}

// Handle returns the found cities by id.
func (handler *FindCitiesHandler) Handle(ctx context.Context, query FindCities) (map[int64]domain.City, error) {
	result := map[int64]domain.City{}
	ids := distinct[int64](query.IDs).values()
	if len(ids) == 0 {
		return result, nil
	}
	cities, err := handler.cities.CitiesByID(ctx, query.Locale, ids)
	if err != nil {
		return nil, err
	}
	for _, city := range cities {
		result[city.ID] = city
	}
	return result, nil
}
