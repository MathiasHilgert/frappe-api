// Package geo is the geo module: read-only geographic reference data
// (countries, first-level subdivisions, cities, IANA time zones) served
// under /v1/geo, localized by the negotiated request language. The data
// is seeded by migration (see migrations/geo.go); this module only reads
// it.
//
// module.go is the module's composition: it builds the Postgres
// repositories, wraps every use case with usecase.NewObserved (span, RED
// metrics and logs named geo.query.<use case>) and registers the HTTP
// handlers on the "/v1" API.
package geo

import (
	"context"

	"github.com/danielgtaylor/huma/v2"

	foundation "github.com/MathiasHilgert/frappe-api/internal/foundation/application"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/rest"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/usecase"
	httpadapter "github.com/MathiasHilgert/frappe-api/internal/modules/geo/adapters/http"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/adapters/postgres"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application/query"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

// Dependencies is what the composition root hands the module.
type Dependencies struct {
	// API is the shared "/v1" group the module registers its operations on.
	API huma.API
	// Pool is the application database pool, read once it is up
	// (*application.Handle[*pgxpool.Pool] from the composition root).
	Pool postgres.PoolSource
	// Cursors signs and verifies pagination cursors.
	Cursors *rest.CursorCodec
}

// Module is the wired geo module. Its only lifecycle hook loads the geo
// data revision at startup, so no request waits for it.
type Module struct {
	revision usecase.QueryHandler[query.GetDataRevision, string]
}

var _ foundation.Module = (*Module)(nil)

// New wires the module and registers its HTTP operations on
// dependencies.API.
func New(dependencies Dependencies) *Module {
	// Repositories are typed as their ports: the use cases only ever see
	// the application interfaces (go-arch-lint checks the injection).
	var (
		countries    application.CountryReader      = postgres.NewCountryRepository(dependencies.Pool)
		subdivisions application.SubdivisionReader  = postgres.NewSubdivisionRepository(dependencies.Pool)
		cities       application.CityReader         = postgres.NewCityRepository(dependencies.Pool)
		timeZones    application.TimeZoneReader     = postgres.NewTimeZoneRepository(dependencies.Pool)
		places       application.PlaceSearcher      = postgres.NewPlaceSearchRepository(dependencies.Pool)
		revisions    application.DataRevisionReader = postgres.NewDataRevisionRepository(dependencies.Pool)
	)
	revision := usecase.NewObserved[query.GetDataRevision, string](query.NewGetDataRevisionHandler(revisions))
	shared := httpadapter.Shared{Revision: revision, Cursors: dependencies.Cursors}
	search := query.SearchReaders{Places: places, Countries: countries, Subdivisions: subdivisions, Cities: cities}
	findCountries := usecase.NewObserved[query.FindCountries, map[string]domain.Country](query.NewFindCountriesHandler(countries))
	findSubdivisions := usecase.NewObserved[query.FindSubdivisions, map[int64]domain.Subdivision](query.NewFindSubdivisionsHandler(subdivisions))
	findCities := usecase.NewObserved[query.FindCities, map[int64]domain.City](query.NewFindCitiesHandler(cities))
	findTimeZones := usecase.NewObserved[query.FindTimeZones, map[string]domain.TimeZone](query.NewFindTimeZonesHandler(timeZones))

	httpadapter.NewCountryHandler(shared, httpadapter.CountryQueries{
		Search:        usecase.NewObserved[query.SearchCountries, query.SearchPage](query.NewSearchCountriesHandler(search), query.ErrQueryTooShort),
		List:          usecase.NewObserved[query.ListCountries, []domain.Country](query.NewListCountriesHandler(countries)),
		Get:           usecase.NewObserved[query.GetCountry, domain.Country](query.NewGetCountryHandler(countries), domain.ErrNotFound),
		FindCities:    findCities,
		FindTimeZones: findTimeZones,
	}).Register(dependencies.API)
	httpadapter.NewSubdivisionHandler(shared, httpadapter.SubdivisionQueries{
		Search:        usecase.NewObserved[query.SearchSubdivisions, query.SearchPage](query.NewSearchSubdivisionsHandler(search), query.ErrQueryTooShort),
		List:          usecase.NewObserved[query.ListSubdivisions, []domain.Subdivision](query.NewListSubdivisionsHandler(subdivisions)),
		Get:           usecase.NewObserved[query.GetSubdivision, domain.Subdivision](query.NewGetSubdivisionHandler(subdivisions), domain.ErrNotFound),
		FindCountries: findCountries,
	}).Register(dependencies.API)
	httpadapter.NewCityHandler(shared, httpadapter.CityQueries{
		Search:           usecase.NewObserved[query.SearchCities, query.SearchPage](query.NewSearchCitiesHandler(search), query.ErrQueryTooShort),
		List:             usecase.NewObserved[query.ListCities, []domain.City](query.NewListCitiesHandler(cities)),
		Get:              usecase.NewObserved[query.GetCity, domain.City](query.NewGetCityHandler(cities), domain.ErrNotFound),
		FindCountries:    findCountries,
		FindSubdivisions: findSubdivisions,
		FindTimeZones:    findTimeZones,
	}).Register(dependencies.API)
	httpadapter.NewTimeZoneHandler(shared, httpadapter.TimeZoneQueries{
		List: usecase.NewObserved[query.ListTimeZones, []domain.TimeZone](query.NewListTimeZonesHandler(timeZones)),
		Get:  usecase.NewObserved[query.GetTimeZone, domain.TimeZone](query.NewGetTimeZoneHandler(timeZones), domain.ErrNotFound),
	}).Register(dependencies.API)

	httpadapter.NewPlaceHandler(shared, httpadapter.PlaceQueries{
		Search: usecase.NewObserved[query.SearchPlaces, query.SearchPage](query.NewSearchPlacesHandler(search), query.ErrQueryTooShort),
	}).Register(dependencies.API)

	return &Module{revision: revision}
}

// Name identifies the module.
func (*Module) Name() string {
	return "geo"
}

// Register appends the hook that loads the geo data revision. It runs
// after the database pool is up and before the HTTP server listens; a
// missing revision (migrations not run) fails startup.
func (module *Module) Register(lifecycle foundation.Lifecycle) error {
	lifecycle.Append(foundation.Hook{Name: "geo.data_revision", Up: module.loadDataRevision})
	return nil
}

func (module *Module) loadDataRevision(ctx context.Context) error {
	_, err := module.revision.Handle(ctx, query.GetDataRevision{})
	return err
}
