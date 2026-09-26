package query

import (
	"context"

	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

// FindTimeZones asks for the time zones with IDs, for example the time
// zones a page of cities expands.
type FindTimeZones struct {
	IDs []string
}

// FindTimeZonesHandler returns the time zones that exist, by id, with one
// read for the whole batch (none for no ids).
type FindTimeZonesHandler struct {
	timeZones application.TimeZoneReader
}

// NewFindTimeZonesHandler returns a FindTimeZonesHandler reading timeZones.
func NewFindTimeZonesHandler(timeZones application.TimeZoneReader) *FindTimeZonesHandler {
	return &FindTimeZonesHandler{timeZones: timeZones}
}

// Handle returns the found time zones by id.
func (handler *FindTimeZonesHandler) Handle(ctx context.Context, query FindTimeZones) (map[string]domain.TimeZone, error) {
	result := map[string]domain.TimeZone{}
	ids := distinct[string](query.IDs).values()
	if len(ids) == 0 {
		return result, nil
	}
	timeZones, err := handler.timeZones.TimeZonesByID(ctx, ids)
	if err != nil {
		return nil, err
	}
	for _, timeZone := range timeZones {
		result[timeZone.ID] = timeZone
	}
	return result, nil
}
