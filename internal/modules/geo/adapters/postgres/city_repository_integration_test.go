//go:build integration

package postgres_test

import (
	"context"
	"testing"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/adapters/postgres"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application"
)

func TestIntegrationCityRepositoryFiltersAndPages(t *testing.T) {
	t.Parallel()
	repository := postgres.NewCityRepository(newReadyPool(t))
	ctx := context.Background()
	province := cordobaProvince
	portuguese := i18n.MustParseLocale("pt-BR")

	first, err := repository.Cities(ctx, portuguese, application.CityFilter{SubdivisionID: &province, Limit: 5})
	if err != nil || len(first) != 5 {
		t.Fatalf("first page = %+v, %v", first, err)
	}
	second, err := repository.Cities(ctx, portuguese, application.CityFilter{CountryCode: "AR", SubdivisionID: &province, After: first[4].ID, Limit: 5})
	if err != nil || len(second) == 0 || second[0].ID <= first[4].ID {
		t.Fatalf("second page = %+v, %v", second, err)
	}
	for _, city := range append(first, second...) {
		if city.CountryCode != "AR" || city.SubdivisionID == nil || *city.SubdivisionID != cordobaProvince {
			t.Fatalf("city outside the filter: %+v", city)
		}
	}
	brazil, err := repository.Cities(ctx, portuguese, application.CityFilter{CountryCode: "BR", Limit: 3})
	if err != nil || len(brazil) != 3 || brazil[0].CountryCode != "BR" {
		t.Fatalf("Brazil = %+v, %v", brazil, err)
	}
}

func TestIntegrationCityRepositoryReadsEveryAttribute(t *testing.T) {
	t.Parallel()
	repository := postgres.NewCityRepository(newReadyPool(t))

	cities, err := repository.CitiesByID(context.Background(), spanish, []int64{cordobaCity, buenosAires, 1})
	if err != nil || len(cities) != 2 {
		t.Fatalf("CitiesByID = %+v, %v", cities, err)
	}
	for _, city := range cities {
		if city.ID != cordobaCity {
			continue
		}
		if city.Name != "Córdoba" || city.TimeZoneID != "America/Argentina/Cordoba" || city.Population < 1_000_000 ||
			city.Latitude > -31 || city.Latitude < -32 || city.Longitude > -64 || city.Longitude < -65 {
			t.Fatalf("Córdoba = %+v", city)
		}
	}
}
