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

func TestGetCountryReturnsTheCountryInTheLocale(t *testing.T) {
	t.Parallel()
	readers := newFakeReaders()

	country, err := query.NewGetCountryHandler(readers).Handle(context.Background(), query.GetCountry{Locale: spanish, Code: "AR"})
	if err != nil || country.Code != "AR" || readers.locale != spanish {
		t.Fatalf("Handle = %+v, %v (locale %v)", country, err, readers.locale)
	}
}

func TestGetCountryReportsAMissingCountryAsNotFound(t *testing.T) {
	t.Parallel()
	_, err := query.NewGetCountryHandler(newFakeReaders()).Handle(context.Background(), query.GetCountry{Code: "ZZ"})
	if !errors.Is(err, domain.ErrNotFound) {
		t.Fatalf("error = %v, want domain.ErrNotFound", err)
	}
}

func TestListCountriesPassesThePageFilter(t *testing.T) {
	t.Parallel()
	readers := newFakeReaders()
	filter := application.CountryFilter{After: "AF", Limit: 11}

	countries, err := query.NewListCountriesHandler(readers).Handle(context.Background(), query.ListCountries{Locale: spanish, Filter: filter})
	if err != nil || len(countries) != 1 || readers.countryFilter != filter {
		t.Fatalf("Handle = %+v, %v (filter %+v)", countries, err, readers.countryFilter)
	}
}

func TestFindCountriesBatchesDistinctCodesInOneRead(t *testing.T) {
	t.Parallel()
	readers := newFakeReaders()
	handler := query.NewFindCountriesHandler(readers)

	found, err := handler.Handle(context.Background(), query.FindCountries{Locale: spanish, Codes: []string{"AR", "ZZ", "AR"}})
	if err != nil || len(found) != 1 || found["AR"].Code != "AR" {
		t.Fatalf("Handle = %+v, %v", found, err)
	}
	if readers.calls["CountriesByCode"] != 1 || !slices.Equal(readers.lastCodes, []string{"AR", "ZZ"}) {
		t.Fatalf("read %d times with %v, want once with distinct codes", readers.calls["CountriesByCode"], readers.lastCodes)
	}
	if _, err := handler.Handle(context.Background(), query.FindCountries{}); err != nil || readers.calls["CountriesByCode"] != 1 {
		t.Fatalf("no codes: err %v, reads %d; want no read", err, readers.calls["CountriesByCode"])
	}
}

func TestCountryQueriesPropagateReadErrors(t *testing.T) {
	t.Parallel()
	readers := newFakeReaders()
	readers.err = errDatabase
	if _, err := query.NewGetCountryHandler(readers).Handle(context.Background(), query.GetCountry{Code: argentinaCode}); !errors.Is(err, errDatabase) {
		t.Errorf("get error = %v", err)
	}
	if _, err := query.NewListCountriesHandler(readers).Handle(context.Background(), query.ListCountries{}); !errors.Is(err, errDatabase) {
		t.Errorf("list error = %v", err)
	}
}
