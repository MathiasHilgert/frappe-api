//go:build integration

package http_test

import (
	"net/http"
	"net/url"
	"testing"
)

func TestIntegrationCitiesExpandTheirCountryAndSubdivision(t *testing.T) {
	t.Parallel()
	api := newIntegrationAPI(t)

	plain := api.get("/v1/geo/cities/3860259", http.StatusOK)
	if plain.body["country"] != "AR" || plain.body["subdivision"] != "3860255" || plain.body["time_zone"] != "America/Argentina/Cordoba" {
		t.Fatalf("unexpanded city = %v", plain.body)
	}
	expanded := api.get("/v1/geo/cities/3860259?expand[]=country&expand[]=subdivision", http.StatusOK, "Accept-Language", "pt-BR")
	country := expanded.body["country"].(map[string]any)
	subdivision := expanded.body["subdivision"].(map[string]any)
	if country["object"] != "country" || country["id"] != "AR" || country["capital_city"] != "3435910" || subdivision["name"] != "Córdova" {
		t.Fatalf("expanded city = %v", expanded.body)
	}
	capitals := api.get("/v1/geo/countries?limit=3&expand[]=capital_city", http.StatusOK)
	for _, item := range api.items(capitals) {
		if capital, ok := item["capital_city"].(map[string]any); ok && capital["object"] != "city" {
			t.Fatalf("capital = %v", capital)
		}
	}
	rejected := api.get("/v1/geo/cities/3860259?expand[]=capital_city&expand[]=Bad", http.StatusUnprocessableEntity)
	if errors := rejected.body["errors"].([]any); len(errors) != 2 {
		t.Fatalf("errors = %v, want one per offending value", errors)
	}
}

func TestIntegrationCityCursorsOnlyWorkOnTheirListing(t *testing.T) {
	t.Parallel()
	api := newIntegrationAPI(t)

	first := api.get("/v1/geo/cities?country=AR&limit=2", http.StatusOK)
	cursor := url.QueryEscape(first.body["next_cursor"].(string))
	for _, target := range []string{
		"/v1/geo/countries?cursor=" + cursor,
		"/v1/geo/cities?country=BR&cursor=" + cursor,
		"/v1/geo/cities?country=AR&cursor=" + cursor + "x",
		"/v1/geo/cities?country=AR&cursor=garbage",
	} {
		result := api.get(target, http.StatusBadRequest)
		if errors := result.body["errors"].([]any); errors[0].(map[string]any)["location"] != "query.cursor" {
			t.Fatalf("GET %s errors = %v, want query.cursor", target, errors)
		}
	}
	api.get("/v1/geo/cities?limit=5&country=AR&cursor="+cursor, http.StatusOK)
	api.revalidates("/v1/geo/cities?country=AR")
	api.notFound("/v1/geo/cities/abc")
	api.notFound("/v1/geo/cities/03860259")
}
