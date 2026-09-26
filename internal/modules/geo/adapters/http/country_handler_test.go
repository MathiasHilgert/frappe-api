package http_test

import (
	"errors"
	"net/http"
	"net/url"
	"testing"

	httpadapter "github.com/MathiasHilgert/frappe-api/internal/modules/geo/adapters/http"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application/query"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

// countryQueries are fakes for every query a CountryHandler uses.
type countryQueries struct {
	list       *fakeQuery[query.ListCountries, []domain.Country]
	get        *fakeQuery[query.GetCountry, domain.Country]
	findCities *fakeQuery[query.FindCities, map[int64]domain.City]
}

func newCountryQueries() countryQueries {
	currency := "ARS"
	argentina := domain.Country{Code: "AR", Name: "Argentina", Alpha3Code: "ARG", NumericCode: 32, ContinentCode: "SA", CurrencyCode: &currency}
	andorra := domain.Country{Code: "AD", Name: "Andorra", Alpha3Code: "AND", NumericCode: 20, ContinentCode: "EU"}
	return countryQueries{
		list:       &fakeQuery[query.ListCountries, []domain.Country]{result: []domain.Country{andorra, argentina}},
		get:        &fakeQuery[query.GetCountry, domain.Country]{result: argentina},
		findCities: &fakeQuery[query.FindCities, map[int64]domain.City]{},
	}
}

func (queries countryQueries) register(api testAPI) {
	httpadapter.NewCountryHandler(api.shared, httpadapter.CountryQueries{List: queries.list, Get: queries.get, FindCities: queries.findCities}).Register(api.api)
}

func TestCountryHandlerGetsACountryByAlpha2Code(t *testing.T) {
	t.Parallel()
	api, queries := newTestAPI(t), newCountryQueries()
	queries.register(api)

	body := api.get("/geo/countries/AR", http.StatusOK)
	if body["object"] != "country" || body["id"] != "AR" || body["numeric_code"] != "032" || body["currency"] != "ARS" || body["continent"] != "SA" {
		t.Fatalf("country = %v", body)
	}
	if _, found := body["native_name"]; found {
		t.Fatalf("country has native_name: %v", body)
	}
	if queries.get.received[0].Code != "AR" {
		t.Fatalf("asked for %+v", queries.get.received)
	}
}

func TestCountryHandlerAnswersNotFoundWithoutAskingForMalformedCodes(t *testing.T) {
	t.Parallel()
	api, queries := newTestAPI(t), newCountryQueries()
	queries.get.err = domain.ErrNotFound
	queries.register(api)

	if body := api.get("/geo/countries/ZZ", http.StatusNotFound); body["detail"] != "No country with id ZZ." {
		t.Fatalf("problem = %v", body)
	}
	api.get("/geo/countries/arg", http.StatusNotFound)
	if len(queries.get.received) != 1 {
		t.Fatalf("asked %d times, want only for the well-formed code", len(queries.get.received))
	}
}

func TestCountryHandlerHidesUnexpectedErrors(t *testing.T) {
	t.Parallel()
	api, queries := newTestAPI(t), newCountryQueries()
	queries.get.err = errors.New("pq: password authentication failed")
	queries.register(api)

	if body := api.get("/geo/countries/AR", http.StatusInternalServerError); body["detail"] != "An unexpected error occurred." {
		t.Fatalf("problem = %v, want the cause hidden", body)
	}
}

func TestCountryHandlerPagesWithACursor(t *testing.T) {
	t.Parallel()
	api, queries := newTestAPI(t), newCountryQueries()
	queries.register(api)

	first := api.get("/geo/countries?limit=1", http.StatusOK)
	items := api.items(first)
	if len(items) != 1 || items[0]["id"] != "AD" || first["has_more"] != true {
		t.Fatalf("first page = %v", first)
	}
	if filter := queries.list.received[0].Filter; filter.Limit != 2 || filter.After != "" {
		t.Fatalf("first filter = %+v, want limit + 1 from the start", filter)
	}
	api.get("/geo/countries?limit=1&cursor="+url.QueryEscape(first["next_cursor"].(string)), http.StatusOK)
	if after := queries.list.received[1].Filter.After; after != "AD" {
		t.Fatalf("second page after %q, want AD", after)
	}
}
