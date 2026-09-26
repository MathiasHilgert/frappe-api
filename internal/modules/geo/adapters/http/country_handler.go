package http

import (
	"context"
	"regexp"

	"github.com/danielgtaylor/huma/v2"

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
	List usecase.QueryHandler[query.ListCountries, []domain.Country]
	Get  usecase.QueryHandler[query.GetCountry, domain.Country]
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
		"Every ISO 3166-1 country and territory, ordered by id (alpha-2 code)."),
		countries.list)
	huma.Register(api, countries.operation("get-country", "/geo/countries/{id}", "Get a country",
		"A country by ISO 3166-1 alpha-2 code."),
		countries.get)
}

func (countries *CountryHandler) list(ctx context.Context, input *ListCountriesInput) (*rest.ListOutput[Country], error) {
	var after string
	if _, err := input.Position(ctx, countries.shared.Cursors, &after); err != nil {
		return nil, err
	}
	locale := countries.locale(ctx)
	if err := countries.revalidate(ctx, locale); err != nil {
		return nil, err
	}
	rows, err := countries.queries.List.Handle(ctx, query.ListCountries{
		Locale: locale, Filter: application.CountryFilter{After: after, Limit: input.Limit + 1},
	})
	if err != nil {
		return nil, countries.failure(ctx, err)
	}
	resources := make([]Country, 0, len(rows))
	for _, row := range rows {
		resources = append(resources, Country{}.from(row))
	}
	return rest.NewPage(countries.shared.Cursors, input.PageParameters, resources, func(last Country) any { return last.ID })
}

func (countries *CountryHandler) get(ctx context.Context, input *GetCountryInput) (*CountryOutput, error) {
	if !countryCodePattern.MatchString(input.ID) {
		return nil, countries.notFound(ctx, "geo.country.not_found", "No country with id", input.ID)
	}
	locale := countries.locale(ctx)
	country, err := countries.queries.Get.Handle(ctx, query.GetCountry{Locale: locale, Code: input.ID})
	if err != nil {
		return nil, countries.lookupFailure(ctx, err, "geo.country.not_found", "No country with id", input.ID)
	}
	if err := countries.revalidate(ctx, locale); err != nil {
		return nil, err
	}
	return &CountryOutput{Body: Country{}.from(country)}, nil
}
