//go:build integration

package http_test

import (
	"net/http"
	"testing"
)

func TestIntegrationCountriesPageAcrossEveryPageWithLinks(t *testing.T) {
	t.Parallel()
	api := newIntegrationAPI(t)

	seen := map[string]bool{}
	pages := 0
	for target := "/v1/geo/countries?limit=100"; target != ""; pages++ {
		result := api.get(target, http.StatusOK)
		if result.body["object"] != "list" || result.body["url"] != "/v1/geo/countries" {
			t.Fatalf("envelope = %v", result.body)
		}
		for _, item := range api.items(result) {
			id := item["id"].(string)
			if seen[id] || item["object"] != "country" {
				t.Fatalf("item %v repeated or not a country", item)
			}
			seen[id] = true
		}
		target = api.nextTarget(result)
	}
	if pages != 3 || len(seen) != 252 {
		t.Fatalf("paged %d countries in %d pages, want 252 in 3", len(seen), pages)
	}
}

func TestIntegrationCountriesFollowTheNegotiatedLanguage(t *testing.T) {
	t.Parallel()
	api := newIntegrationAPI(t)

	japanese := api.get("/v1/geo/countries/AR", http.StatusOK, "Accept-Language", "ja")
	if japanese.body["name"] != "アルゼンチン" || japanese.header.Get("Content-Language") != "ja" {
		t.Fatalf("AR in ja = %v (%s)", japanese.body, japanese.header.Get("Content-Language"))
	}
}

func TestIntegrationCountriesAreCacheableAndLocalizeNotFound(t *testing.T) {
	t.Parallel()
	api := newIntegrationAPI(t)

	api.revalidates("/v1/geo/countries/AR")
	api.revalidates("/v1/geo/countries?limit=5")
	api.notFound("/v1/geo/countries/ar")
	if problem := api.notFound("/v1/geo/countries/ZZ"); problem.body["detail"] != "No existe un país con id ZZ." {
		t.Fatalf("problem = %v, want the detail in the negotiated language", problem.body)
	}
}
