package query_test

import (
	"context"
	"errors"
	"slices"
	"testing"

	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application/query"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

func TestSearchRejectsQueriesShorterThanTheMinimum(t *testing.T) {
	t.Parallel()
	readers := newFakeReaders()

	for _, text := range []string{"", " a ", "é"} {
		if _, err := query.NewSearchPlacesHandler(readers.searchReaders()).Handle(context.Background(), query.SearchPlaces{Text: text, Limit: 10}); !errors.Is(err, query.ErrQueryTooShort) {
			t.Fatalf("Search(%q) error = %v, want ErrQueryTooShort", text, err)
		}
	}
	if readers.calls["Search"] != 0 {
		t.Fatalf("searched %d times for too short queries", readers.calls["Search"])
	}
}

func TestSearchPlacesLoadsEachKindInOneBatchAndKeepsTheRanking(t *testing.T) {
	t.Parallel()
	readers := newFakeReaders()
	readers.matches = []application.Match{
		{Kind: domain.KindSubdivision, PlaceID: cordobaProvince, Position: application.SearchPosition{Score: 1, KindRank: 1, PlaceID: cordobaProvince}},
		{Kind: domain.KindCity, PlaceID: cordobaCity, Position: application.SearchPosition{Score: 1, KindRank: 2, Population: 1428214, PlaceID: cordobaCity}},
		{Kind: domain.KindCountry, PlaceID: 3865483, CountryCode: "AR", Position: application.SearchPosition{Score: 0.5, PlaceID: 3865483}},
		{Kind: domain.KindCity, PlaceID: 404, Position: application.SearchPosition{Score: 0.4, KindRank: 2, PlaceID: 404}},
	}

	page, err := query.NewSearchPlacesHandler(readers.searchReaders()).Handle(context.Background(),
		query.SearchPlaces{Text: " Cordoba ", Locale: spanish, CountryCode: "AR", Limit: 10})
	if err != nil {
		t.Fatalf("Search: %v", err)
	}
	if search := readers.lastSearch; search.Text != "Cordoba" || search.Limit != 11 || search.CountryCode != "AR" || len(search.Kinds) != 0 {
		t.Fatalf("searched %+v, want the trimmed text, limit + 1, the filter and every kind", search)
	}
	if len(page.Results) != 3 || page.Next != nil {
		t.Fatalf("page = %+v, want 3 results (a vanished place is skipped) and no next page", page)
	}
	if page.Results[0].Place.Subdivision == nil || page.Results[1].Place.City == nil || page.Results[2].Place.Country == nil {
		t.Fatalf("results out of ranking order: %+v", page.Results)
	}
	for _, method := range []string{"CitiesByID", "SubdivisionsByID", "CountriesByCode"} {
		if readers.calls[method] != 1 {
			t.Fatalf("%s called %d times, want one batch", method, readers.calls[method])
		}
	}
}

func TestSearchDerivesTheNextPageFromTheRawMatches(t *testing.T) {
	t.Parallel()
	readers := newFakeReaders()
	vanished := application.SearchPosition{Score: 0.9, KindRank: 2, PlaceID: 404}
	readers.matches = []application.Match{
		{Kind: domain.KindCity, PlaceID: cordobaCity, Position: application.SearchPosition{Score: 1, KindRank: 2, PlaceID: cordobaCity}},
		{Kind: domain.KindCity, PlaceID: 404, Position: vanished},
		{Kind: domain.KindSubdivision, PlaceID: cordobaProvince, Position: application.SearchPosition{Score: 0.8, KindRank: 1, PlaceID: cordobaProvince}},
	}
	after := application.SearchPosition{Score: 2, PlaceID: 1}

	page, err := query.NewSearchPlacesHandler(readers.searchReaders()).Handle(context.Background(),
		query.SearchPlaces{Text: "cordoba", After: &after, Limit: 2})
	if err != nil {
		t.Fatalf("Search: %v", err)
	}
	if len(page.Results) != 1 || page.Results[0].Place.City == nil {
		t.Fatalf("results = %+v, want the one loadable match of the page", page.Results)
	}
	if page.Next == nil || *page.Next != vanished || readers.lastSearch.After != &after {
		t.Fatalf("next = %+v, want the last raw match of the page even though it vanished", page.Next)
	}
}

func TestTypedSearchesKeepTheirKindAndFilters(t *testing.T) {
	t.Parallel()
	readers := newFakeReaders()
	province := cordobaProvince
	ctx := context.Background()

	if _, err := query.NewSearchCountriesHandler(readers.searchReaders()).Handle(ctx, query.SearchCountries{Text: "argentina", Limit: 5}); err != nil ||
		!slices.Equal(readers.lastSearch.Kinds, []domain.PlaceKind{domain.KindCountry}) {
		t.Fatalf("countries searched %+v, %v", readers.lastSearch, err)
	}
	if _, err := query.NewSearchSubdivisionsHandler(readers.searchReaders()).Handle(ctx, query.SearchSubdivisions{Text: "cordoba", CountryCode: "AR", Limit: 5}); err != nil ||
		!slices.Equal(readers.lastSearch.Kinds, []domain.PlaceKind{domain.KindSubdivision}) || readers.lastSearch.CountryCode != "AR" {
		t.Fatalf("subdivisions searched %+v, %v", readers.lastSearch, err)
	}
	if _, err := query.NewSearchCitiesHandler(readers.searchReaders()).Handle(ctx, query.SearchCities{Text: "villa", CountryCode: "AR", SubdivisionID: &province, Limit: 5}); err != nil ||
		!slices.Equal(readers.lastSearch.Kinds, []domain.PlaceKind{domain.KindCity}) || *readers.lastSearch.SubdivisionID != province {
		t.Fatalf("cities searched %+v, %v", readers.lastSearch, err)
	}
}

func TestSearchPropagatesReadErrors(t *testing.T) {
	t.Parallel()
	readers := newFakeReaders()
	readers.err = errDatabase

	if _, err := query.NewSearchPlacesHandler(readers.searchReaders()).Handle(context.Background(), query.SearchPlaces{Text: "cordoba", Limit: 5}); !errors.Is(err, errDatabase) {
		t.Fatalf("error = %v, want the read error", err)
	}
}
