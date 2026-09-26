package application

import (
	"context"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

// SubdivisionFilter selects a page of subdivisions ordered by id: those
// after After (0 for the first page), at most Limit, optionally only of
// CountryCode and with ISOCode (empty means any).
type SubdivisionFilter struct {
	CountryCode string
	ISOCode     string
	After       int64
	Limit       int
}

// SubdivisionReader reads subdivisions. Subdivisions returns rows in id
// order; SubdivisionsByID returns the ones that exist, in any order.
type SubdivisionReader interface {
	Subdivisions(ctx context.Context, locale i18n.Locale, filter SubdivisionFilter) ([]domain.Subdivision, error)
	SubdivisionsByID(ctx context.Context, locale i18n.Locale, ids []int64) ([]domain.Subdivision, error)
}
