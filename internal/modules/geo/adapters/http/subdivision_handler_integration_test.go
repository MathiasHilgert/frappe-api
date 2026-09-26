//go:build integration

package http_test

import (
	"net/http"
	"testing"
)

func TestIntegrationSubdivisionsFollowTheNegotiatedLanguage(t *testing.T) {
	t.Parallel()
	api := newIntegrationAPI(t)

	for language, want := range map[string]string{"ja": "コルドバ州", "pt-BR": "Córdova", "pt": "Córdova"} {
		result := api.get("/v1/geo/subdivisions/3860255", http.StatusOK, "Accept-Language", language)
		if result.body["name"] != want || result.body["iso_code"] != "AR-X" || result.body["country"] != "AR" ||
			result.body["object"] != "subdivision" || result.body["id"] != "3860255" {
			t.Fatalf("AR-X in %s = %v, want name %q", language, result.body, want)
		}
		if _, found := result.body["native_name"]; found {
			t.Fatalf("subdivision has native_name: %v", result.body)
		}
	}
}

func TestIntegrationSubdivisionsFilterByISOCodeAndExpandTheCountry(t *testing.T) {
	t.Parallel()
	api := newIntegrationAPI(t)

	result := api.get("/v1/geo/subdivisions?country=AR&iso_code=AR-X&expand[]=country", http.StatusOK)
	items := api.items(result)
	if len(items) != 1 || items[0]["id"] != "3860255" || items[0]["country"].(map[string]any)["numeric_code"] != "032" {
		t.Fatalf("subdivisions = %v", result.body)
	}
	api.revalidates("/v1/geo/subdivisions?country=AR")
	api.notFound("/v1/geo/subdivisions/1")
	api.notFound("/v1/geo/subdivisions/AR-X")
	api.get("/v1/geo/subdivisions?iso_code=ARX", http.StatusUnprocessableEntity)
}
