//go:build integration

package http_test

import (
	"net/http"
	"net/url"
	"testing"
)

func TestIntegrationSearchRanksTolerantlyAcrossKinds(t *testing.T) {
	t.Parallel()
	api := newIntegrationAPI(t)

	cases := []struct {
		target, object, id string
	}{
		{"/v1/geo/places/search?query=cordoba", "subdivision", "3860255"},
		{"/v1/geo/places/search?query=sao%20paulo", "subdivision", "3448433"},
		{"/v1/geo/places/search?query=buenos%20aries&country=AR", "subdivision", "3433955"},
		{"/v1/geo/countries/search?query=argentina", "country", "AR"},
		{"/v1/geo/subdivisions/search?query=cordoba&country=AR", "subdivision", "3860255"},
		{"/v1/geo/cities/search?query=cordoba&subdivision=3860255", "city", "3860259"},
	}
	for _, testCase := range cases {
		items := api.items(api.get(testCase.target, http.StatusOK))
		if len(items) == 0 || items[0]["object"] != testCase.object || items[0]["id"] != testCase.id {
			t.Fatalf("GET %s first = %v, want %s %s", testCase.target, items, testCase.object, testCase.id)
		}
	}
	kinds := map[any]bool{}
	for _, item := range api.items(api.get("/v1/geo/places/search?query=cordoba&country=AR&limit=20", http.StatusOK)) {
		kinds[item["object"]] = true
	}
	if !kinds["city"] || !kinds["subdivision"] {
		t.Fatalf("mixed search kinds = %v, want cities and the province", kinds)
	}
}

func TestIntegrationSearchPagesWithoutOverlapAndRejectsShortQueries(t *testing.T) {
	t.Parallel()
	api := newIntegrationAPI(t)

	first := api.get("/v1/geo/places/search?query=san&country=AR&limit=3", http.StatusOK)
	second := api.get("/v1/geo/places/search?query=san&country=AR&limit=3&cursor="+url.QueryEscape(first.body["next_cursor"].(string)), http.StatusOK)
	for _, earlier := range api.items(first) {
		for _, later := range api.items(second) {
			if earlier["id"] == later["id"] {
				t.Fatalf("search pages overlap on %v", earlier["id"])
			}
		}
	}
	for _, target := range []string{
		"/v1/geo/places/search?query=a",
		"/v1/geo/places/search?query=%20%20%20",
		"/v1/geo/places/search",
		"/v1/geo/cities/search?query=cordoba&country=arg",
	} {
		api.get(target, http.StatusUnprocessableEntity)
	}
	short := api.get("/v1/geo/places/search?query=%20%20%20", http.StatusUnprocessableEntity, "Accept-Language", "es-419")
	if short.body["detail"] != "La búsqueda es demasiado corta." {
		t.Fatalf("problem = %v, want the localized detail", short.body)
	}
	api.revalidates("/v1/geo/places/search?query=cordoba")
}
