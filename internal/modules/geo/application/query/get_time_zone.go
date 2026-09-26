package query

import (
	"context"

	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

// GetTimeZone asks for one time zone by IANA id.
type GetTimeZone struct {
	ID string
}

// GetTimeZoneHandler returns the time zone, or domain.ErrNotFound.
type GetTimeZoneHandler struct {
	timeZones application.TimeZoneReader
}

// NewGetTimeZoneHandler returns a GetTimeZoneHandler reading timeZones.
func NewGetTimeZoneHandler(timeZones application.TimeZoneReader) *GetTimeZoneHandler {
	return &GetTimeZoneHandler{timeZones: timeZones}
}

// Handle returns the time zone with query.ID.
func (handler *GetTimeZoneHandler) Handle(ctx context.Context, query GetTimeZone) (domain.TimeZone, error) {
	timeZones, err := handler.timeZones.TimeZonesByID(ctx, []string{query.ID})
	if err != nil {
		return domain.TimeZone{}, err
	}
	for _, timeZone := range timeZones {
		if timeZone.ID == query.ID {
			return timeZone, nil
		}
	}
	return domain.TimeZone{}, domain.ErrNotFound
}
