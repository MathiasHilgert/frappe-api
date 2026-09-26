package query_test

import (
	"context"
	"errors"
	"slices"
	"testing"

	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application/query"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

func TestGetCityReturnsItOrNotFound(t *testing.T) {
	t.Parallel()
	handler := query.NewGetCityHandler(newFakeReaders())

	city, err := handler.Handle(context.Background(), query.GetCity{Locale: spanish, ID: cordobaCity})
	if err != nil || city.Population != 1428214 {
		t.Fatalf("Handle = %+v, %v", city, err)
	}
	if _, err := handler.Handle(context.Background(), query.GetCity{ID: 1}); !errors.Is(err, domain.ErrNotFound) {
		t.Fatalf("missing error = %v, want domain.ErrNotFound", err)
	}
}

func TestListCitiesPassesTheFilter(t *testing.T) {
	t.Parallel()
	readers := newFakeReaders()
	province := cordobaProvince
	filter := application.CityFilter{CountryCode: "AR", SubdivisionID: &province, After: 5, Limit: 11}

	cities, err := query.NewListCitiesHandler(readers).Handle(context.Background(), query.ListCities{Locale: spanish, Filter: filter})
	if err != nil || len(cities) != 1 || readers.cityFilter != filter {
		t.Fatalf("Handle = %+v, %v (filter %+v)", cities, err, readers.cityFilter)
	}
}

func TestFindCitiesBatchesDistinctIDs(t *testing.T) {
	t.Parallel()
	readers := newFakeReaders()

	found, err := query.NewFindCitiesHandler(readers).Handle(context.Background(),
		query.FindCities{Locale: spanish, IDs: []int64{cordobaCity, cordobaCity, 2}})
	if err != nil || len(found) != 1 || found[cordobaCity].Name != "Córdoba" {
		t.Fatalf("Handle = %+v, %v", found, err)
	}
	if readers.calls["CitiesByID"] != 1 || !slices.Equal(readers.lastIDs, []int64{cordobaCity, 2}) {
		t.Fatalf("read %d times with %v", readers.calls["CitiesByID"], readers.lastIDs)
	}
}
