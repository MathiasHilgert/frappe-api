//go:build integration

package http_test

import (
	"net/http"
	"testing"
)

func TestIntegrationTimeZonesKeepSlashesInTheirID(t *testing.T) {
	t.Parallel()
	api := newIntegrationAPI(t)

	result := api.get("/v1/geo/time_zones/America/Argentina/Cordoba", http.StatusOK)
	if result.body["id"] != "America/Argentina/Cordoba" || result.body["country"] != "AR" || result.body["object"] != "time_zone" {
		t.Fatalf("time zone = %v", result.body)
	}
	if list := api.get("/v1/geo/time_zones?limit=2", http.StatusOK); len(api.items(list)) != 2 || list.body["has_more"] != true {
		t.Fatalf("time zones = %v", list.body)
	}
	api.revalidates("/v1/geo/time_zones/America/Argentina/Cordoba")
	api.notFound("/v1/geo/time_zones/Mars/Olympus_Mons")
}

func TestIntegrationCountriesAndCitiesExpandTheirTimeZones(t *testing.T) {
	t.Parallel()
	api := newIntegrationAPI(t)

	city := api.get("/v1/geo/cities/3860259?expand[]=time_zone", http.StatusOK)
	if timeZone := city.body["time_zone"].(map[string]any); timeZone["object"] != "time_zone" || timeZone["country"] != "AR" {
		t.Fatalf("city time zone = %v", city.body["time_zone"])
	}
	countries := api.get("/v1/geo/countries?limit=3&expand[]=default_time_zone", http.StatusOK)
	for _, item := range api.items(countries) {
		if zone, ok := item["default_time_zone"].(map[string]any); !ok || zone["object"] != "time_zone" {
			t.Fatalf("default_time_zone = %v", item["default_time_zone"])
		}
	}
}
