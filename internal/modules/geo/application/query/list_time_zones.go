package query

import (
	"context"

	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

// ListTimeZones asks for one page of time zones ordered by id.
type ListTimeZones struct {
	Filter application.TimeZoneFilter
}

// ListTimeZonesHandler returns a page of time zones.
type ListTimeZonesHandler struct {
	timeZones application.TimeZoneReader
}

// NewListTimeZonesHandler returns a ListTimeZonesHandler reading timeZones.
func NewListTimeZonesHandler(timeZones application.TimeZoneReader) *ListTimeZonesHandler {
	return &ListTimeZonesHandler{timeZones: timeZones}
}

// Handle returns the page query.Filter selects.
func (handler *ListTimeZonesHandler) Handle(ctx context.Context, query ListTimeZones) ([]domain.TimeZone, error) {
	return handler.timeZones.TimeZones(ctx, query.Filter)
}
