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

// CityQueries are the use cases the city operations run.
type CityQueries struct {
	List             usecase.QueryHandler[query.ListCities, []domain.City]
	Get              usecase.QueryHandler[query.GetCity, domain.City]
	FindCountries    usecase.QueryHandler[query.FindCountries, map[string]domain.Country]
	FindSubdivisions usecase.QueryHandler[query.FindSubdivisions, map[int64]domain.Subdivision]
}

// CityHandler serves /geo/cities.
type CityHandler struct {
	queries CityQueries
	handler
}

// NewCityHandler returns a CityHandler running queries.
func NewCityHandler(shared Shared, queries CityQueries) *CityHandler {
	return &CityHandler{handler: handler{shared: shared}, queries: queries}
}

// Register adds the city operations onto api.
func (cities *CityHandler) Register(api huma.API) {
	huma.Register(api, cities.operation("list-cities", "/geo/cities", "List cities",
		"Cities ordered by id. Filter by country and subdivision. Expandable: country, subdivision."),
		cities.list)
	huma.Register(api, cities.operation("get-city", "/geo/cities/{id}", "Get a city",
		"A city by GeoNames id. Expandable: country, subdivision."),
		cities.get)
}

func (cities *CityHandler) list(ctx context.Context, input *ListCitiesInput) (*rest.ListOutput[City], error) {
	expand, err := cityExpansions.Parse(ctx, input.Expand)
	if err != nil {
		return nil, err
	}
	var after int64
	if _, err = input.Position(ctx, cities.shared.Cursors, &after); err != nil {
		return nil, err
	}
	locale := cities.locale(ctx)
	if err = cities.revalidate(ctx, locale); err != nil {
		return nil, err
	}
	rows, err := cities.queries.List.Handle(ctx, query.ListCities{Locale: locale, Filter: application.CityFilter{
		CountryCode: input.Country, SubdivisionID: input.subdivisionID(), After: after, Limit: input.Limit + 1,
	}})
	if err != nil {
		return nil, cities.failure(ctx, err)
	}
	resources, err := cities.resources(ctx, locale, rows, expand)
	if err != nil {
		return nil, err
	}
	return rest.NewPage(cities.shared.Cursors, input.PageParameters, resources, func(last City) any {
		id, _ := cities.placeID(last.ID)
		return id
	})
}

func (cities *CityHandler) get(ctx context.Context, input *GetCityInput) (*CityOutput, error) {
	expand, err := cityExpansions.Parse(ctx, input.Expand)
	if err != nil {
		return nil, err
	}
	id, valid := cities.placeID(input.ID)
	if !valid {
		return nil, cities.notFound(ctx, "geo.city.not_found", "No city with id", input.ID)
	}
	locale := cities.locale(ctx)
	city, err := cities.queries.Get.Handle(ctx, query.GetCity{Locale: locale, ID: id})
	if err != nil {
		return nil, cities.lookupFailure(ctx, err, "geo.city.not_found", "No city with id", input.ID)
	}
	if err = cities.revalidate(ctx, locale); err != nil {
		return nil, err
	}
	resources, err := cities.resources(ctx, locale, []domain.City{city}, expand)
	if err != nil {
		return nil, err
	}
	return &CityOutput{Body: resources[0]}, nil
}

// cityRelations are the expanded relations of one page of cities, loaded
// with one batch per relation.
type cityRelations struct {
	countries    map[string]domain.Country
	subdivisions map[int64]domain.Subdivision
}

func (cities *CityHandler) relations(ctx context.Context, locale i18n.Locale, rows []domain.City, expand rest.Expand) (cityRelations, error) {
	var relations cityRelations
	var err error
	if expand.Has(expandCountry) {
		codes := make([]string, 0, len(rows))
		for _, row := range rows {
			codes = append(codes, row.CountryCode)
		}
		if relations.countries, err = cities.queries.FindCountries.Handle(ctx, query.FindCountries{Locale: locale, Codes: codes}); err != nil {
			return cityRelations{}, err
		}
	}
	if expand.Has(expandSubdivision) {
		ids := make([]int64, 0, len(rows))
		for _, row := range rows {
			if row.SubdivisionID != nil {
				ids = append(ids, *row.SubdivisionID)
			}
		}
		if relations.subdivisions, err = cities.queries.FindSubdivisions.Handle(ctx, query.FindSubdivisions{Locale: locale, IDs: ids}); err != nil {
			return cityRelations{}, err
		}
	}
	return relations, nil
}

// resources presents rows with the expanded relations inlined.
func (cities *CityHandler) resources(ctx context.Context, locale i18n.Locale, rows []domain.City, expand rest.Expand) ([]City, error) {
	relations, err := cities.relations(ctx, locale, rows, expand)
	if err != nil {
		return nil, cities.failure(ctx, err)
	}
	resources := make([]City, 0, len(rows))
	for _, row := range rows {
		resource := City{}.from(row)
		if country, found := relations.countries[row.CountryCode]; found {
			resource.Country = rest.ExpandedResource(country.Code, Country{}.from(country))
		}
		if row.SubdivisionID != nil {
			if subdivision, found := relations.subdivisions[*row.SubdivisionID]; found {
				resource.Subdivision = rest.NullableExpandedResource(cities.placeIDText(subdivision.ID), Subdivision{}.from(subdivision))
			}
		}
		resources = append(resources, resource)
	}
	return resources, nil
}
