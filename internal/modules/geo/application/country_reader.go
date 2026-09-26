package application

import (
	"context"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

// CountryFilter selects a page of countries ordered by code: those after
// After (empty for the first page), at most Limit.
type CountryFilter struct {
	After string
	Limit int
}

// CountryReader reads countries. Countries returns rows in code order;
// CountriesByCode returns the ones that exist, in any order.
type CountryReader interface {
	Countries(ctx context.Context, locale i18n.Locale, filter CountryFilter) ([]domain.Country, error)
	CountriesByCode(ctx context.Context, locale i18n.Locale, codes []string) ([]domain.Country, error)
}
