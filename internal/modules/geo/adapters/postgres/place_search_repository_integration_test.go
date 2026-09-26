//go:build integration

package postgres_test

import (
	"context"
	"testing"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/adapters/postgres"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

func TestIntegrationPlaceSearchRanksAccentlessAndTypoQueries(t *testing.T) {
	t.Parallel()
	repository := postgres.NewPlaceSearchRepository(newReadyPool(t))

	cases := []struct {
		name  string
		text  string
		first int64
	}{
		{name: "accentless", text: "cordoba", first: cordobaCity},
		{name: "accentless with a space", text: "sao paulo", first: saoPaulo},
		{name: "typo", text: "buenos aries", first: buenosAires},
		{name: "prefix", text: "buenos ai", first: buenosAires},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			matches, err := repository.Search(context.Background(), application.SearchQuery{
				Text: testCase.text, Locale: spanish, Kinds: []domain.PlaceKind{domain.KindCity}, Limit: 5,
			})
			if err != nil {
				t.Fatalf("Search: %v", err)
			}
			if len(matches) == 0 || matches[0].PlaceID != testCase.first || matches[0].Kind != domain.KindCity {
				t.Fatalf("Search(%q) = %+v, want %d first", testCase.text, matches, testCase.first)
			}
			for index := 1; index < len(matches); index++ {
				if matches[index].Position.Score > matches[index-1].Position.Score {
					t.Fatalf("matches not ranked by score: %+v", matches)
				}
			}
		})
	}
}

func TestIntegrationPlaceSearchBreaksTiesByKindThenPopulation(t *testing.T) {
	t.Parallel()
	repository := postgres.NewPlaceSearchRepository(newReadyPool(t))

	matches, err := repository.Search(context.Background(), application.SearchQuery{Text: "cordoba", Locale: spanish, CountryCode: "AR", Limit: 3})
	if err != nil || len(matches) < 2 {
		t.Fatalf("Search(cordoba) = %+v, %v", matches, err)
	}
	// The province and the city share their name, so their score: the
	// subdivision ranks first by kind.
	if matches[0].PlaceID != cordobaProvince || matches[1].PlaceID != cordobaCity || matches[0].Position.Score != matches[1].Position.Score {
		t.Fatalf("Search(cordoba) = %+v, want the province, then the city", matches)
	}
	if matches[0].Position.Population == 0 || matches[0].Position.KindRank != 1 || matches[1].Position.KindRank != 2 {
		t.Fatalf("positions = %+v, want the province's population and the kind ranks", matches)
	}
}

func TestIntegrationPlaceSearchUsesTheLocaleNamesAndFilters(t *testing.T) {
	t.Parallel()
	repository := postgres.NewPlaceSearchRepository(newReadyPool(t))
	ctx := context.Background()

	// "Córdova" is only the Portuguese name of the province.
	matches, err := repository.Search(ctx, application.SearchQuery{
		Text: "cordova", Locale: i18n.MustParseLocale("pt-BR"), Kinds: []domain.PlaceKind{domain.KindSubdivision}, CountryCode: "AR", Limit: 5,
	})
	if err != nil || len(matches) == 0 || matches[0].PlaceID != cordobaProvince {
		t.Fatalf("Search(cordova, pt-BR) = %+v, %v", matches, err)
	}
	countries, err := repository.Search(ctx, application.SearchQuery{Text: "argentina", Locale: spanish, Kinds: []domain.PlaceKind{domain.KindCountry}, Limit: 5})
	if err != nil || len(countries) == 0 || countries[0].CountryCode != "AR" || countries[0].Kind != domain.KindCountry {
		t.Fatalf("Search(argentina, countries) = %+v, %v", countries, err)
	}

	province := cordobaProvince
	cities, err := repository.Search(ctx, application.SearchQuery{
		Text: "villa", Locale: spanish, Kinds: []domain.PlaceKind{domain.KindCity}, SubdivisionID: &province, Limit: 50,
	})
	if err != nil || len(cities) == 0 {
		t.Fatalf("Search(villa in AR-X) = %+v, %v", cities, err)
	}
	identifiers := make([]int64, 0, len(cities))
	for _, match := range cities {
		identifiers = append(identifiers, match.PlaceID)
	}
	loaded, err := postgres.NewCityRepository(newReadyPool(t)).CitiesByID(ctx, spanish, identifiers)
	if err != nil || len(loaded) != len(identifiers) {
		t.Fatalf("CitiesByID = %d cities, %v", len(loaded), err)
	}
	for _, city := range loaded {
		if city.SubdivisionID == nil || *city.SubdivisionID != cordobaProvince {
			t.Fatalf("city outside the subdivision filter: %+v", city)
		}
	}
}

func TestIntegrationPlaceSearchPagesWithoutOverlap(t *testing.T) {
	t.Parallel()
	repository := postgres.NewPlaceSearchRepository(newReadyPool(t))
	ctx := context.Background()
	search := application.SearchQuery{Text: "san", Locale: spanish, CountryCode: "AR", Limit: 20}

	all, err := repository.Search(ctx, application.SearchQuery{Text: search.Text, Locale: search.Locale, CountryCode: "AR", Limit: 40})
	if err != nil || len(all) != 40 {
		t.Fatalf("Search(san) = %d matches, %v", len(all), err)
	}
	first, err := repository.Search(ctx, search)
	if err != nil {
		t.Fatalf("first page: %v", err)
	}
	search.After = &first[len(first)-1].Position
	second, err := repository.Search(ctx, search)
	if err != nil {
		t.Fatalf("second page: %v", err)
	}
	paged := append(append([]application.Match{}, first...), second...)
	for index := range all {
		if paged[index].PlaceID != all[index].PlaceID {
			t.Fatalf("page boundary broke the ranking at %d: %d, want %d", index, paged[index].PlaceID, all[index].PlaceID)
		}
	}
}
