package application

import (
	"context"

	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

// TimeZoneFilter selects a page of time zones ordered by id: those after
// After (empty for the first page), at most Limit.
type TimeZoneFilter struct {
	After string
	Limit int
}

// TimeZoneReader reads IANA time zones, which have no localized names.
// TimeZones returns rows in id order; TimeZonesByID returns the ones that
// exist, in any order.
type TimeZoneReader interface {
	TimeZones(ctx context.Context, filter TimeZoneFilter) ([]domain.TimeZone, error)
	TimeZonesByID(ctx context.Context, ids []string) ([]domain.TimeZone, error)
}
