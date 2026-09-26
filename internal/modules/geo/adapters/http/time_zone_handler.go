package http

import (
	"context"

	"github.com/danielgtaylor/huma/v2"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/rest"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/usecase"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application/query"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

// TimeZoneQueries are the use cases the time zone operations run.
type TimeZoneQueries struct {
	List usecase.QueryHandler[query.ListTimeZones, []domain.TimeZone]
	Get  usecase.QueryHandler[query.GetTimeZone, domain.TimeZone]
}

// TimeZoneHandler serves /geo/time_zones.
type TimeZoneHandler struct {
	queries TimeZoneQueries
	handler
}

// NewTimeZoneHandler returns a TimeZoneHandler running queries.
func NewTimeZoneHandler(shared Shared, queries TimeZoneQueries) *TimeZoneHandler {
	return &TimeZoneHandler{handler: handler{shared: shared}, queries: queries}
}

// Register adds the time zone operations onto api.
func (timeZones *TimeZoneHandler) Register(api huma.API) {
	huma.Register(api, timeZones.operation("list-time-zones", "/geo/time_zones", "List time zones",
		"IANA time zones, ordered by id."),
		timeZones.list)
	huma.Register(api, timeZones.operation("get-time-zone", "/geo/time_zones/{id...}", "Get a time zone",
		"A time zone by IANA id, for example /v1/geo/time_zones/America/Argentina/Cordoba."),
		timeZones.get)
}

func (timeZones *TimeZoneHandler) list(ctx context.Context, input *ListTimeZonesInput) (*rest.ListOutput[TimeZone], error) {
	var after string
	if _, err := input.Position(ctx, timeZones.shared.Cursors, &after); err != nil {
		return nil, err
	}
	if err := timeZones.revalidate(ctx, timeZones.locale(ctx)); err != nil {
		return nil, err
	}
	rows, err := timeZones.queries.List.Handle(ctx, query.ListTimeZones{Filter: application.TimeZoneFilter{After: after, Limit: input.Limit + 1}})
	if err != nil {
		return nil, timeZones.failure(ctx, err)
	}
	resources := make([]TimeZone, 0, len(rows))
	for _, row := range rows {
		resources = append(resources, TimeZone{}.from(row))
	}
	return rest.NewPage(timeZones.shared.Cursors, input.PageParameters, resources, func(last TimeZone) any { return last.ID })
}

func (timeZones *TimeZoneHandler) get(ctx context.Context, input *GetTimeZoneInput) (*TimeZoneOutput, error) {
	timeZone, err := timeZones.queries.Get.Handle(ctx, query.GetTimeZone{ID: input.ID})
	if err != nil {
		return nil, timeZones.lookupFailure(ctx, err, "geo.time_zone.not_found", "No time zone with id", input.ID)
	}
	if err = timeZones.revalidate(ctx, timeZones.locale(ctx)); err != nil {
		return nil, err
	}
	return &TimeZoneOutput{Body: TimeZone{}.from(timeZone)}, nil
}
