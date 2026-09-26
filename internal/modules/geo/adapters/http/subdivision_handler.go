package http

import (
	"context"

	"github.com/danielgtaylor/huma/v2"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/rest"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/usecase"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application/query"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

// SubdivisionQueries are the use cases the subdivision operations run.
type SubdivisionQueries struct {
	Search        usecase.QueryHandler[query.SearchSubdivisions, query.SearchPage]
	List          usecase.QueryHandler[query.ListSubdivisions, []domain.Subdivision]
	Get           usecase.QueryHandler[query.GetSubdivision, domain.Subdivision]
	FindCountries usecase.QueryHandler[query.FindCountries, map[string]domain.Country]
}

// SubdivisionHandler serves /geo/subdivisions.
type SubdivisionHandler struct {
	queries SubdivisionQueries
	handler
}

// NewSubdivisionHandler returns a SubdivisionHandler running queries.
func NewSubdivisionHandler(shared Shared, queries SubdivisionQueries) *SubdivisionHandler {
	return &SubdivisionHandler{handler: handler{shared: shared}, queries: queries}
}

// Register adds the subdivision operations onto api.
func (subdivisions *SubdivisionHandler) Register(api huma.API) {
	huma.Register(api, subdivisions.operation("list-subdivisions", "/geo/subdivisions", "List subdivisions",
		"First-level subdivisions (provinces, states, regions), ordered by id. Filter by country and ISO 3166-2 code. Expandable: country."),
		subdivisions.list)
	huma.Register(api, subdivisions.operation("search-subdivisions", "/geo/subdivisions/search", "Search subdivisions",
		"Subdivisions whose name (own or in the response language) matches query, best match first. Filter by country. Expandable: country."),
		subdivisions.search)
	huma.Register(api, subdivisions.operation("get-subdivision", "/geo/subdivisions/{id}", "Get a subdivision",
		"A subdivision by GeoNames id. Expandable: country."),
		subdivisions.get)
}

func (subdivisions *SubdivisionHandler) list(ctx context.Context, input *ListSubdivisionsInput) (*rest.ListOutput[Subdivision], error) {
	expand, err := subdivisionExpansions.Parse(ctx, input.Expand)
	if err != nil {
		return nil, err
	}
	var after int64
	if _, err = input.Position(ctx, subdivisions.shared.Cursors, &after); err != nil {
		return nil, err
	}
	locale := subdivisions.locale(ctx)
	if err = subdivisions.revalidate(ctx, locale); err != nil {
		return nil, err
	}
	rows, err := subdivisions.queries.List.Handle(ctx, query.ListSubdivisions{Locale: locale, Filter: application.SubdivisionFilter{
		CountryCode: input.Country, ISOCode: input.ISOCode, After: after, Limit: input.Limit + 1,
	}})
	if err != nil {
		return nil, subdivisions.failure(ctx, err)
	}
	resources, err := subdivisions.resources(ctx, locale, rows, expand)
	if err != nil {
		return nil, err
	}
	return rest.NewPage(subdivisions.shared.Cursors, input.PageParameters, resources, func(last Subdivision) any {
		id, _ := subdivisions.placeID(last.ID)
		return id
	})
}

func (subdivisions *SubdivisionHandler) get(ctx context.Context, input *GetSubdivisionInput) (*SubdivisionOutput, error) {
	expand, err := subdivisionExpansions.Parse(ctx, input.Expand)
	if err != nil {
		return nil, err
	}
	id, valid := subdivisions.placeID(input.ID)
	if !valid {
		return nil, subdivisions.notFound(ctx, "geo.subdivision.not_found", "No subdivision with id", input.ID)
	}
	locale := subdivisions.locale(ctx)
	subdivision, err := subdivisions.queries.Get.Handle(ctx, query.GetSubdivision{Locale: locale, ID: id})
	if err != nil {
		return nil, subdivisions.lookupFailure(ctx, err, "geo.subdivision.not_found", "No subdivision with id", input.ID)
	}
	if err = subdivisions.revalidate(ctx, locale); err != nil {
		return nil, err
	}
	resources, err := subdivisions.resources(ctx, locale, []domain.Subdivision{subdivision}, expand)
	if err != nil {
		return nil, err
	}
	return &SubdivisionOutput{Body: resources[0]}, nil
}

func (subdivisions *SubdivisionHandler) search(ctx context.Context, input *SearchSubdivisionsInput) (*rest.ListOutput[Subdivision], error) {
	expand, err := subdivisionExpansions.Parse(ctx, input.Expand)
	if err != nil {
		return nil, err
	}
	after, err := subdivisions.searchAfter(ctx, input.PageParameters)
	if err != nil {
		return nil, err
	}
	locale := subdivisions.locale(ctx)
	if err = subdivisions.revalidate(ctx, locale); err != nil {
		return nil, err
	}
	page, err := subdivisions.queries.Search.Handle(ctx, query.SearchSubdivisions{
		Locale: locale, After: after, Text: input.Query, CountryCode: input.Country, Limit: input.Limit,
	})
	if err != nil {
		return nil, subdivisions.failure(ctx, err)
	}
	rows := make([]domain.Subdivision, 0, len(page.Results))
	for _, result := range page.Results {
		rows = append(rows, *result.Place.Subdivision)
	}
	resources, err := subdivisions.resources(ctx, locale, rows, expand)
	if err != nil {
		return nil, err
	}
	return rest.NewCursorPage(subdivisions.shared.Cursors, input.PageParameters, resources, subdivisions.next(page))
}

// resources presents rows, loading every expanded country of the page in
// one batch.
func (subdivisions *SubdivisionHandler) resources(ctx context.Context, locale i18n.Locale, rows []domain.Subdivision, expand rest.Expand) ([]Subdivision, error) {
	var countries map[string]domain.Country
	if expand.Has(expandCountry) {
		codes := make([]string, 0, len(rows))
		for _, row := range rows {
			codes = append(codes, row.CountryCode)
		}
		var err error
		if countries, err = subdivisions.queries.FindCountries.Handle(ctx, query.FindCountries{Locale: locale, Codes: codes}); err != nil {
			return nil, subdivisions.failure(ctx, err)
		}
	}
	resources := make([]Subdivision, 0, len(rows))
	for _, row := range rows {
		resource := Subdivision{}.from(row)
		if country, found := countries[row.CountryCode]; found {
			resource.Country = rest.ExpandedResource(country.Code, Country{}.from(country))
		}
		resources = append(resources, resource)
	}
	return resources, nil
}
