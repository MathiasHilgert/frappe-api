package application

import (
	"context"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

// CityFilter selects a page of cities ordered by id: those after After
// (0 for the first page), at most Limit, optionally only of CountryCode
// (empty means any) and SubdivisionID (nil means any).
type CityFilter struct {
	SubdivisionID *int64
	CountryCode   string
	After         int64
	Limit         int
}

// CityReader reads cities. Cities returns rows in id order; CitiesByID
// returns the ones that exist, in any order.
type CityReader interface {
	Cities(ctx context.Context, locale i18n.Locale, filter CityFilter) ([]domain.City, error)
	CitiesByID(ctx context.Context, locale i18n.Locale, ids []int64) ([]domain.City, error)
}
