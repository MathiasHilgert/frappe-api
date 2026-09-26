package http_test

import (
	"net/http"
	"testing"

	httpadapter "github.com/MathiasHilgert/frappe-api/internal/modules/geo/adapters/http"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application/query"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

// cityQueries are fakes for every query a CityHandler uses.
type cityQueries struct {
	list             *fakeQuery[query.ListCities, []domain.City]
	get              *fakeQuery[query.GetCity, domain.City]
	findCountries    *fakeQuery[query.FindCountries, map[string]domain.Country]
	findSubdivisions *fakeQuery[query.FindSubdivisions, map[int64]domain.Subdivision]
	findTimeZones    *fakeQuery[query.FindTimeZones, map[string]domain.TimeZone]
}

func newCityQueries() cityQueries {
	province := int64(3860255)
	cordoba := domain.City{
		ID: 3860259, Name: "Córdoba", CountryCode: "AR", SubdivisionID: &province,
		TimeZoneID: "America/Argentina/Cordoba", Population: 1428214, Latitude: -31.4135, Longitude: -64.18105,
	}
	return cityQueries{
		list: &fakeQuery[query.ListCities, []domain.City]{result: []domain.City{cordoba}},
		get:  &fakeQuery[query.GetCity, domain.City]{result: cordoba},
		findCountries: &fakeQuery[query.FindCountries, map[string]domain.Country]{result: map[string]domain.Country{
			"AR": {Code: "AR", Name: "Argentina"},
		}},
		findSubdivisions: &fakeQuery[query.FindSubdivisions, map[int64]domain.Subdivision]{result: map[int64]domain.Subdivision{
			province: {ID: province, Name: "Córdova", CountryCode: "AR"},
		}},
		findTimeZones: &fakeQuery[query.FindTimeZones, map[string]domain.TimeZone]{},
	}
}

func (queries cityQueries) register(api testAPI) {
	httpadapter.NewCityHandler(api.shared, httpadapter.CityQueries{
		List: queries.list, Get: queries.get, FindCountries: queries.findCountries, FindSubdivisions: queries.findSubdivisions, FindTimeZones: queries.findTimeZones,
	}).Register(api.api)
}

func TestCityHandlerGetsACityWithItsRelationsAsIDs(t *testing.T) {
	t.Parallel()
	api, queries := newTestAPI(t), newCityQueries()
	queries.register(api)

	body := api.get("/geo/cities/3860259", http.StatusOK)
	if body["object"] != "city" || body["id"] != "3860259" || body["country"] != "AR" || body["subdivision"] != "3860255" ||
		body["time_zone"] != "America/Argentina/Cordoba" || body["population"] != float64(1428214) || body["latitude"] != -31.4135 {
		t.Fatalf("city = %v", body)
	}
}

func TestCityHandlerExpandsCountryAndSubdivisionInOneBatchEach(t *testing.T) {
	t.Parallel()
	api, queries := newTestAPI(t), newCityQueries()
	queries.register(api)

	body := api.get("/geo/cities?country=AR&subdivision=3860255&expand[]=country&expand[]=subdivision", http.StatusOK)
	item := api.items(body)[0]
	if item["country"].(map[string]any)["object"] != "country" || item["subdivision"].(map[string]any)["name"] != "Córdova" {
		t.Fatalf("expanded city = %v", item)
	}
	filter := queries.list.received[0].Filter
	if filter.CountryCode != "AR" || filter.SubdivisionID == nil || *filter.SubdivisionID != 3860255 {
		t.Fatalf("filter = %+v", filter)
	}
	if len(queries.findCountries.received) != 1 || len(queries.findSubdivisions.received) != 1 {
		t.Fatalf("expanded in %d and %d reads, want one batch each", len(queries.findCountries.received), len(queries.findSubdivisions.received))
	}
}

func TestCityHandlerRejectsMalformedIDsAndFilters(t *testing.T) {
	t.Parallel()
	api, queries := newTestAPI(t), newCityQueries()
	queries.register(api)

	api.get("/geo/cities/03860259", http.StatusNotFound)
	api.get("/geo/cities?subdivision=abc", http.StatusUnprocessableEntity)
	api.get("/geo/cities?country=arg", http.StatusUnprocessableEntity)
	if len(queries.get.received)+len(queries.list.received) != 0 {
		t.Fatalf("queries ran for malformed requests")
	}
}

func TestCountryHandlerExpandsTheCapitalCity(t *testing.T) {
	t.Parallel()
	api, queries := newTestAPI(t), newCountryQueries()
	capital := int64(3435910)
	queries.get.result.CapitalCityID = &capital
	queries.findCities.result = map[int64]domain.City{capital: {ID: capital, Name: "Buenos Aires", CountryCode: "AR"}}
	queries.register(api)

	if body := api.get("/geo/countries/AR", http.StatusOK); body["capital_city"] != "3435910" {
		t.Fatalf("unexpanded capital = %v", body["capital_city"])
	}
	body := api.get("/geo/countries/AR?expand[]=capital_city", http.StatusOK)
	if capitalCity := body["capital_city"].(map[string]any); capitalCity["object"] != "city" || capitalCity["name"] != "Buenos Aires" {
		t.Fatalf("expanded capital = %v", body["capital_city"])
	}
	if body := api.get("/geo/countries?limit=5", http.StatusOK); api.items(body)[0]["capital_city"] != nil {
		t.Fatalf("a country without a capital = %v, want null", api.items(body)[0])
	}
}
