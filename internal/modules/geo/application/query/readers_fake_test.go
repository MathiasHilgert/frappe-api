package query_test

import (
	"context"
	"errors"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application/query"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

var (
	spanish       = i18n.MustParseLocale("es-419")
	errDatabase   = errors.New("database down")
	argentinaCode = "AR"
)

// GeoNames ids of the fixtures.
const (
	cordobaProvince = int64(3860255)
	cordobaCity     = int64(3860259)
)

// fakeReaders implements every geo port on in-memory data and records how
// it was called.
type fakeReaders struct {
	countries         map[string]domain.Country
	subdivisions      map[int64]domain.Subdivision
	cities            map[int64]domain.City
	calls             map[string]int
	err               error
	revisionError     error
	locale            i18n.Locale
	revision          string
	lastCodes         []string
	countryFilter     application.CountryFilter
	lastIDs           []int64
	subdivisionFilter application.SubdivisionFilter
	cityFilter        application.CityFilter
	timeZoneFilter    application.TimeZoneFilter
	lastTimeZoneIDs   []string
	matches           []application.Match
	lastSearch        application.SearchQuery
}

func newFakeReaders() *fakeReaders {
	currency, isoCode := "ARS", "AR-X"
	return &fakeReaders{
		cities: map[int64]domain.City{
			cordobaCity: {ID: cordobaCity, Name: "Córdoba", CountryCode: "AR", TimeZoneID: "America/Argentina/Cordoba", Population: 1428214},
		},
		subdivisions: map[int64]domain.Subdivision{
			cordobaProvince: {ID: cordobaProvince, Name: "Córdoba", ISOCode: &isoCode, CountryCode: "AR"},
		},
		calls: map[string]int{},
		countries: map[string]domain.Country{
			"AR": {Code: "AR", PlaceID: 3865483, Name: "Argentina", CurrencyCode: &currency},
		},
	}
}

func (readers *fakeReaders) Countries(_ context.Context, locale i18n.Locale, filter application.CountryFilter) ([]domain.Country, error) {
	readers.calls["Countries"]++
	readers.locale, readers.countryFilter = locale, filter
	if readers.err != nil {
		return nil, readers.err
	}
	return []domain.Country{readers.countries["AR"]}, nil
}

func (readers *fakeReaders) CountriesByCode(_ context.Context, locale i18n.Locale, codes []string) ([]domain.Country, error) {
	readers.calls["CountriesByCode"]++
	readers.locale, readers.lastCodes = locale, codes
	if readers.err != nil {
		return nil, readers.err
	}
	var result []domain.Country
	for _, code := range codes {
		if country, found := readers.countries[code]; found {
			result = append(result, country)
		}
	}
	return result, nil
}

func (readers *fakeReaders) DataRevision(context.Context) (string, error) {
	readers.calls["DataRevision"]++
	return readers.revision, readers.revisionError
}

func (readers *fakeReaders) Subdivisions(_ context.Context, locale i18n.Locale, filter application.SubdivisionFilter) ([]domain.Subdivision, error) {
	readers.calls["Subdivisions"]++
	readers.locale, readers.subdivisionFilter = locale, filter
	return []domain.Subdivision{readers.subdivisions[cordobaProvince]}, readers.err
}

func (readers *fakeReaders) SubdivisionsByID(_ context.Context, locale i18n.Locale, ids []int64) ([]domain.Subdivision, error) {
	readers.calls["SubdivisionsByID"]++
	readers.locale, readers.lastIDs = locale, ids
	if readers.err != nil {
		return nil, readers.err
	}
	var result []domain.Subdivision
	for _, id := range ids {
		if subdivision, found := readers.subdivisions[id]; found {
			result = append(result, subdivision)
		}
	}
	return result, nil
}

func (readers *fakeReaders) Cities(_ context.Context, locale i18n.Locale, filter application.CityFilter) ([]domain.City, error) {
	readers.calls["Cities"]++
	readers.locale, readers.cityFilter = locale, filter
	return []domain.City{readers.cities[cordobaCity]}, readers.err
}

func (readers *fakeReaders) CitiesByID(_ context.Context, locale i18n.Locale, ids []int64) ([]domain.City, error) {
	readers.calls["CitiesByID"]++
	readers.locale, readers.lastIDs = locale, ids
	if readers.err != nil {
		return nil, readers.err
	}
	var result []domain.City
	for _, id := range ids {
		if city, found := readers.cities[id]; found {
			result = append(result, city)
		}
	}
	return result, nil
}

func (readers *fakeReaders) TimeZones(_ context.Context, filter application.TimeZoneFilter) ([]domain.TimeZone, error) {
	readers.calls["TimeZones"]++
	readers.timeZoneFilter = filter
	return []domain.TimeZone{{ID: "America/Argentina/Cordoba", CountryCode: &argentinaCode}}, readers.err
}

func (readers *fakeReaders) TimeZonesByID(_ context.Context, ids []string) ([]domain.TimeZone, error) {
	readers.calls["TimeZonesByID"]++
	readers.lastTimeZoneIDs = ids
	if readers.err != nil {
		return nil, readers.err
	}
	var result []domain.TimeZone
	for _, id := range ids {
		if id == "America/Argentina/Cordoba" {
			result = append(result, domain.TimeZone{ID: id, CountryCode: &argentinaCode})
		}
	}
	return result, nil
}

func (readers *fakeReaders) Search(_ context.Context, query application.SearchQuery) ([]application.Match, error) {
	readers.calls["Search"]++
	readers.lastSearch = query
	matches := readers.matches
	if len(matches) > query.Limit {
		matches = matches[:query.Limit]
	}
	return matches, readers.err
}

// searchReaders are readers as every port a search needs.
func (readers *fakeReaders) searchReaders() query.SearchReaders {
	return query.SearchReaders{Places: readers, Countries: readers, Subdivisions: readers, Cities: readers}
}
