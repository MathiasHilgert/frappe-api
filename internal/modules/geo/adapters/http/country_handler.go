package http

import (
	"context"
	"regexp"

	"github.com/danielgtaylor/huma/v2"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/rest"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/usecase"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application/query"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

// countryCodePattern is an ISO 3166-1 alpha-2 code.
var countryCodePattern = regexp.MustCompile(`^[A-Z]{2}$`)

// CountryQueries are the use cases the country operations run.
type CountryQueries struct {
	List          usecase.QueryHandler[query.ListCountries, []domain.Country]
	Get           usecase.QueryHandler[query.GetCountry, domain.Country]
	FindCities    usecase.QueryHandler[query.FindCities, map[int64]domain.City]
	FindTimeZones usecase.QueryHandler[query.FindTimeZones, map[string]domain.TimeZone]
}

// CountryHandler serves /geo/countries.
type CountryHandler struct {
	queries CountryQueries
	handler
}

// NewCountryHandler returns a CountryHandler running queries.
func NewCountryHandler(shared Shared, queries CountryQueries) *CountryHandler {
	return &CountryHandler{handler: handler{shared: shared}, queries: queries}
}

// Register adds the country operations onto api.
func (countries *CountryHandler) Register(api huma.API) {
	huma.Register(api, countries.operation("list-countries", "/geo/countries", "List countries",
		"Every ISO 3166-1 country and territory, ordered by id (alpha-2 code). Expandable: capital_city, default_time_zone."),
		countries.list)
	huma.Register(api, countries.operation("get-country", "/geo/countries/{id}", "Get a country",
		"A country by ISO 3166-1 alpha-2 code. Expandable: capital_city, default_time_zone."),
		countries.get)
}

func (countries *CountryHandler) list(ctx context.Context, input *ListCountriesInput) (*rest.ListOutput[Country], error) {
	expand, err := countryExpansions.Parse(ctx, input.Expand)
	if err != nil {
		return nil, err
	}
	var after string
	if _, err = input.Position(ctx, countries.shared.Cursors, &after); err != nil {
		return nil, err
	}
	locale := countries.locale(ctx)
	if err = countries.revalidate(ctx, locale); err != nil {
		return nil, err
	}
	rows, err := countries.queries.List.Handle(ctx, query.ListCountries{
		Locale: locale, Filter: application.CountryFilter{After: after, Limit: input.Limit + 1},
	})
	if err != nil {
		return nil, countries.failure(ctx, err)
	}
	resources, err := countries.resources(ctx, locale, rows, expand)
	if err != nil {
		return nil, err
	}
	return rest.NewPage(countries.shared.Cursors, input.PageParameters, resources, func(last Country) any { return last.ID })
}

func (countries *CountryHandler) get(ctx context.Context, input *GetCountryInput) (*CountryOutput, error) {
	expand, err := countryExpansions.Parse(ctx, input.Expand)
	if err != nil {
		return nil, err
	}
	if !countryCodePattern.MatchString(input.ID) {
		return nil, countries.notFound(ctx, "geo.country.not_found", "No country with id", input.ID)
	}
	locale := countries.locale(ctx)
	country, err := countries.queries.Get.Handle(ctx, query.GetCountry{Locale: locale, Code: input.ID})
	if err != nil {
		return nil, countries.lookupFailure(ctx, err, "geo.country.not_found", "No country with id", input.ID)
	}
	if err = countries.revalidate(ctx, locale); err != nil {
		return nil, err
	}
	resources, err := countries.resources(ctx, locale, []domain.Country{country}, expand)
	if err != nil {
		return nil, err
	}
	return &CountryOutput{Body: resources[0]}, nil
}

// countryRelations are the expanded relations of one page of countries,
// loaded with one batch per relation.
type countryRelations struct {
	capitals  map[int64]domain.City
	timeZones map[string]domain.TimeZone
}

func (countries *CountryHandler) relations(ctx context.Context, locale i18n.Locale, rows []domain.Country, expand rest.Expand) (countryRelations, error) {
	var relations countryRelations
	var err error
	if expand.Has(expandCapitalCity) {
		if relations.capitals, err = countries.capitals(ctx, locale, rows); err != nil {
			return countryRelations{}, err
		}
	}
	if expand.Has(expandDefaultTimeZone) {
		if relations.timeZones, err = countries.timeZones(ctx, rows); err != nil {
			return countryRelations{}, err
		}
	}
	return relations, nil
}

func (countries *CountryHandler) capitals(ctx context.Context, locale i18n.Locale, rows []domain.Country) (map[int64]domain.City, error) {
	ids := make([]int64, 0, len(rows))
	for _, row := range rows {
		if row.CapitalCityID != nil {
			ids = append(ids, *row.CapitalCityID)
		}
	}
	return countries.queries.FindCities.Handle(ctx, query.FindCities{Locale: locale, IDs: ids})
}

func (countries *CountryHandler) timeZones(ctx context.Context, rows []domain.Country) (map[string]domain.TimeZone, error) {
	ids := make([]string, 0, len(rows))
	for _, row := range rows {
		if row.DefaultTimeZoneID != nil {
			ids = append(ids, *row.DefaultTimeZoneID)
		}
	}
	return countries.queries.FindTimeZones.Handle(ctx, query.FindTimeZones{IDs: ids})
}

// resources presents rows with the expanded relations inlined.
func (countries *CountryHandler) resources(ctx context.Context, locale i18n.Locale, rows []domain.Country, expand rest.Expand) ([]Country, error) {
	relations, err := countries.relations(ctx, locale, rows, expand)
	if err != nil {
		return nil, countries.failure(ctx, err)
	}
	resources := make([]Country, 0, len(rows))
	for _, row := range rows {
		resource := Country{}.from(row)
		if row.CapitalCityID != nil {
			if capital, found := relations.capitals[*row.CapitalCityID]; found {
				resource.CapitalCity = rest.NullableExpandedResource(countries.placeIDText(capital.ID), City{}.from(capital))
			}
		}
		if row.DefaultTimeZoneID != nil {
			if timeZone, found := relations.timeZones[*row.DefaultTimeZoneID]; found {
				resource.DefaultTimeZone = rest.NullableExpandedResource(timeZone.ID, TimeZone{}.from(timeZone))
			}
		}
		resources = append(resources, resource)
	}
	return resources, nil
}
