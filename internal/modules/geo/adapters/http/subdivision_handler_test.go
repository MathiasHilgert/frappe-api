package http_test

import (
	"net/http"
	"testing"

	httpadapter "github.com/MathiasHilgert/frappe-api/internal/modules/geo/adapters/http"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application/query"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

// subdivisionQueries are fakes for every query a SubdivisionHandler uses.
type subdivisionQueries struct {
	list          *fakeQuery[query.ListSubdivisions, []domain.Subdivision]
	get           *fakeQuery[query.GetSubdivision, domain.Subdivision]
	findCountries *fakeQuery[query.FindCountries, map[string]domain.Country]
	search        *fakeQuery[query.SearchSubdivisions, query.SearchPage]
}

func newSubdivisionQueries() subdivisionQueries {
	isoCode := "AR-X"
	cordoba := domain.Subdivision{ID: 3860255, Name: "Córdoba", ISOCode: &isoCode, CountryCode: "AR"}
	return subdivisionQueries{
		list: &fakeQuery[query.ListSubdivisions, []domain.Subdivision]{result: []domain.Subdivision{cordoba}},
		get:  &fakeQuery[query.GetSubdivision, domain.Subdivision]{result: cordoba},
		findCountries: &fakeQuery[query.FindCountries, map[string]domain.Country]{result: map[string]domain.Country{
			"AR": {Code: "AR", Name: "Argentina", NumericCode: 32},
		}},
		search: &fakeQuery[query.SearchSubdivisions, query.SearchPage]{},
	}
}

func (queries subdivisionQueries) register(api testAPI) {
	httpadapter.NewSubdivisionHandler(api.shared, httpadapter.SubdivisionQueries{
		Search: queries.search,
		List:   queries.list, Get: queries.get, FindCountries: queries.findCountries,
	}).Register(api.api)
}

func TestSubdivisionHandlerGetsBySubdivisionIDWithTheCountryUnexpanded(t *testing.T) {
	t.Parallel()
	api, queries := newTestAPI(t), newSubdivisionQueries()
	queries.register(api)

	body := api.get("/geo/subdivisions/3860255", http.StatusOK)
	if body["object"] != "subdivision" || body["id"] != "3860255" || body["iso_code"] != "AR-X" || body["country"] != "AR" {
		t.Fatalf("subdivision = %v", body)
	}
	if queries.get.received[0].ID != 3860255 || len(queries.findCountries.received) != 0 {
		t.Fatalf("asked %+v and expanded %d times", queries.get.received, len(queries.findCountries.received))
	}
}

func TestSubdivisionHandlerExpandsTheCountryInOneBatch(t *testing.T) {
	t.Parallel()
	api, queries := newTestAPI(t), newSubdivisionQueries()
	queries.register(api)

	body := api.get("/geo/subdivisions?country=AR&iso_code=AR-X&expand[]=country", http.StatusOK)
	country := api.items(body)[0]["country"].(map[string]any)
	if country["object"] != "country" || country["numeric_code"] != "032" {
		t.Fatalf("expanded country = %v", country)
	}
	if filter := queries.list.received[0].Filter; filter.CountryCode != "AR" || filter.ISOCode != "AR-X" || filter.Limit != 11 {
		t.Fatalf("filter = %+v", filter)
	}
	if len(queries.findCountries.received) != 1 {
		t.Fatalf("expanded in %d reads, want one batch", len(queries.findCountries.received))
	}
}

func TestSubdivisionHandlerRejectsMalformedIDsAndUnknownExpansions(t *testing.T) {
	t.Parallel()
	api, queries := newTestAPI(t), newSubdivisionQueries()
	queries.register(api)

	api.get("/geo/subdivisions/AR-X", http.StatusNotFound)
	api.get("/geo/subdivisions/0", http.StatusNotFound)
	api.get("/geo/subdivisions/3860255?expand[]=capital_city", http.StatusUnprocessableEntity)
	if len(queries.get.received) != 0 {
		t.Fatalf("asked %d times, want never", len(queries.get.received))
	}
}
