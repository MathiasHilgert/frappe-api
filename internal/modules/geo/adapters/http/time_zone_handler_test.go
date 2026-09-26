package http_test

import (
	"net/http"
	"testing"

	httpadapter "github.com/MathiasHilgert/frappe-api/internal/modules/geo/adapters/http"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application/query"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

const cordobaZone = "America/Argentina/Cordoba"

// timeZoneQueries are fakes for every query a TimeZoneHandler uses.
type timeZoneQueries struct {
	list *fakeQuery[query.ListTimeZones, []domain.TimeZone]
	get  *fakeQuery[query.GetTimeZone, domain.TimeZone]
}

func newTimeZoneQueries() timeZoneQueries {
	argentina := "AR"
	zone := domain.TimeZone{ID: cordobaZone, CountryCode: &argentina}
	return timeZoneQueries{
		list: &fakeQuery[query.ListTimeZones, []domain.TimeZone]{result: []domain.TimeZone{{ID: "Africa/Abidjan"}, zone}},
		get:  &fakeQuery[query.GetTimeZone, domain.TimeZone]{result: zone},
	}
}

func (queries timeZoneQueries) register(api testAPI) {
	httpadapter.NewTimeZoneHandler(api.shared, httpadapter.TimeZoneQueries{List: queries.list, Get: queries.get}).Register(api.api)
}

func TestTimeZoneHandlerKeepsSlashesInTheID(t *testing.T) {
	t.Parallel()
	api, queries := newTestAPI(t), newTimeZoneQueries()
	queries.register(api)

	body := api.get("/geo/time_zones/America/Argentina/Cordoba", http.StatusOK)
	if body["object"] != "time_zone" || body["id"] != cordobaZone || body["country"] != "AR" || queries.get.received[0].ID != cordobaZone {
		t.Fatalf("time zone = %v (asked %+v)", body, queries.get.received)
	}
	queries.get.err = domain.ErrNotFound
	api.get("/geo/time_zones/Mars/Olympus_Mons", http.StatusNotFound)
}

func TestTimeZoneHandlerPagesByID(t *testing.T) {
	t.Parallel()
	api, queries := newTestAPI(t), newTimeZoneQueries()
	queries.register(api)

	body := api.get("/geo/time_zones?limit=1", http.StatusOK)
	if items := api.items(body); len(items) != 1 || items[0]["country"] != nil || body["has_more"] != true {
		t.Fatalf("time zones = %v", body)
	}
}

func TestCityAndCountryHandlersExpandTimeZones(t *testing.T) {
	t.Parallel()
	api, cities, countries := newTestAPI(t), newCityQueries(), newCountryQueries()
	argentina := "AR"
	zones := map[string]domain.TimeZone{cordobaZone: {ID: cordobaZone, CountryCode: &argentina}}
	cities.findTimeZones.result, countries.findTimeZones.result = zones, zones
	zone := cordobaZone
	countries.get.result.DefaultTimeZoneID = &zone
	cities.register(api)
	countries.register(api)

	city := api.get("/geo/cities/3860259?expand[]=time_zone", http.StatusOK)
	if timeZone := city["time_zone"].(map[string]any); timeZone["object"] != "time_zone" || timeZone["country"] != "AR" {
		t.Fatalf("expanded city time zone = %v", city["time_zone"])
	}
	country := api.get("/geo/countries/AR?expand[]=default_time_zone", http.StatusOK)
	if timeZone := country["default_time_zone"].(map[string]any); timeZone["id"] != cordobaZone {
		t.Fatalf("expanded default time zone = %v", country["default_time_zone"])
	}
}
