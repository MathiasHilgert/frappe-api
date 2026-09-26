package http_test

import (
	"errors"
	"fmt"
	"net/http"
	"net/url"
	"testing"

	"github.com/danielgtaylor/huma/v2"

	httpadapter "github.com/MathiasHilgert/frappe-api/internal/modules/geo/adapters/http"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application/query"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

// mixedPage is a search page with one place of each kind and a next page.
func (api testAPI) mixedPage() query.SearchPage {
	isoCode := "AR-X"
	next := application.SearchPosition{Score: 0.5, KindRank: 2, PlaceID: 3860259}
	return query.SearchPage{Next: &next, Results: []query.SearchResult{
		{Place: domain.Place{Kind: domain.KindCountry, Country: &domain.Country{Code: "AR", Name: "Argentina"}}},
		{Place: domain.Place{Kind: domain.KindSubdivision, Subdivision: &domain.Subdivision{ID: 3860255, ISOCode: &isoCode, CountryCode: "AR"}}},
		{Place: domain.Place{Kind: domain.KindCity, City: &domain.City{ID: 3860259, CountryCode: "AR", TimeZoneID: "America/Argentina/Cordoba"}}},
	}}
}

func TestPlaceHandlerSearchesEveryKindAndPagesFromTheRawMatches(t *testing.T) {
	t.Parallel()
	api := newTestAPI(t)
	search := &fakeQuery[query.SearchPlaces, query.SearchPage]{result: api.mixedPage()}
	httpadapter.NewPlaceHandler(api.shared, httpadapter.PlaceQueries{Search: search}).Register(api.api)

	first := api.get("/geo/places/search?query=cordoba&country=AR&limit=3", http.StatusOK)
	items := api.items(first)
	if len(items) != 3 || items[0]["object"] != "country" || items[1]["object"] != "subdivision" || items[2]["object"] != "city" {
		t.Fatalf("places = %v", items)
	}
	if received := search.received[0]; received.Text != "cordoba" || received.CountryCode != "AR" || received.Limit != 3 || received.After != nil {
		t.Fatalf("searched %+v", received)
	}
	if first["has_more"] != true {
		t.Fatalf("has_more = %v, want true from the raw matches", first["has_more"])
	}
	search.result.Next = nil
	last := api.get("/geo/places/search?query=cordoba&country=AR&limit=3&cursor="+url.QueryEscape(first["next_cursor"].(string)), http.StatusOK)
	if after := search.received[1].After; after == nil || after.PlaceID != 3860259 || last["has_more"] != false {
		t.Fatalf("second page after %+v, has_more %v", after, last["has_more"])
	}
}

func TestPlaceHandlerAnswersShortQueriesWithALocalizedProblem(t *testing.T) {
	t.Parallel()
	api := newTestAPI(t)
	search := &fakeQuery[query.SearchPlaces, query.SearchPage]{err: fmt.Errorf("search: %w", query.ErrQueryTooShort)}
	httpadapter.NewPlaceHandler(api.shared, httpadapter.PlaceQueries{Search: search}).Register(api.api)

	body := api.get("/geo/places/search?query=%20%20%20", http.StatusUnprocessableEntity)
	detail := body["errors"].([]any)[0].(map[string]any)
	if body["detail"] != "The search query is too short." || detail["location"] != "query.query" {
		t.Fatalf("problem = %v", body)
	}
	api.get("/geo/places/search?query=a", http.StatusUnprocessableEntity)
	api.get("/geo/places/search", http.StatusUnprocessableEntity)
	search.err = errors.New("timeout")
	api.get("/geo/places/search?query=cordoba", http.StatusInternalServerError)
}

func TestPlaceHandlerDeclaresPlacesAsADiscriminatedOneOf(t *testing.T) {
	t.Parallel()
	api := newTestAPI(t)
	httpadapter.NewPlaceHandler(api.shared, httpadapter.PlaceQueries{Search: &fakeQuery[query.SearchPlaces, query.SearchPage]{}}).Register(api.api)

	var place *huma.Schema
	for _, schema := range api.api.OpenAPI().Components.Schemas.Map() {
		if items := schema.Properties["data"]; items != nil && items.Items != nil && items.Items.Discriminator != nil {
			place = items.Items
		}
	}
	if place == nil || len(place.OneOf) != 3 || place.Discriminator == nil || place.Discriminator.PropertyName != "object" {
		t.Fatalf("Place schema = %+v, want a oneOf discriminated by object", place)
	}
	for _, object := range []string{"country", "subdivision", "city"} {
		if place.Discriminator.Mapping[object] == "" {
			t.Fatalf("discriminator mapping = %v, want %s", place.Discriminator.Mapping, object)
		}
	}
}

func TestResourceSearchesKeepTheirKindAndExpansions(t *testing.T) {
	t.Parallel()
	api, countries, subdivisions, cities := newTestAPI(t), newCountryQueries(), newSubdivisionQueries(), newCityQueries()
	page := api.mixedPage()
	countries.search.result = query.SearchPage{Results: page.Results[:1]}
	subdivisions.search.result = query.SearchPage{Results: page.Results[1:2]}
	cities.search.result = query.SearchPage{Results: page.Results[2:]}
	countries.register(api)
	subdivisions.register(api)
	cities.register(api)

	if items := api.items(api.get("/geo/countries/search?query=arg", http.StatusOK)); items[0]["id"] != "AR" {
		t.Fatalf("countries = %v", items)
	}
	subdivision := api.items(api.get("/geo/subdivisions/search?query=cordoba&country=AR&expand[]=country", http.StatusOK))[0]
	if subdivision["country"].(map[string]any)["object"] != "country" || subdivisions.search.received[0].CountryCode != "AR" {
		t.Fatalf("subdivisions = %v", subdivision)
	}
	api.get("/geo/cities/search?query=cordoba&subdivision=3860255", http.StatusOK)
	if received := cities.search.received[0]; received.SubdivisionID == nil || *received.SubdivisionID != 3860255 {
		t.Fatalf("cities searched %+v", received)
	}
}
