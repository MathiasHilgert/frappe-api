//go:build integration

package postgres_test

import (
	"context"
	"testing"

	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/adapters/postgres"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application"
)

func TestIntegrationCountryRepositoryPagesByCode(t *testing.T) {
	t.Parallel()
	repository := postgres.NewCountryRepository(newReadyPool(t))
	ctx := context.Background()

	first, err := repository.Countries(ctx, spanish, application.CountryFilter{Limit: 3})
	if err != nil {
		t.Fatalf("Countries: %v", err)
	}
	if len(first) != 3 || first[0].Code != "AD" || first[1].Code != "AE" || first[2].Code != "AF" {
		t.Fatalf("first page = %+v", first)
	}
	second, err := repository.Countries(ctx, spanish, application.CountryFilter{After: first[2].Code, Limit: 1})
	if err != nil || len(second) != 1 || second[0].Code != "AG" {
		t.Fatalf("second page = %+v, %v", second, err)
	}
}

func TestIntegrationCountryRepositoryLocalizesNames(t *testing.T) {
	t.Parallel()
	repository := postgres.NewCountryRepository(newReadyPool(t))

	countries, err := repository.CountriesByCode(context.Background(), japanese, []string{"AR", "BR", "ZZ"})
	if err != nil {
		t.Fatalf("CountriesByCode: %v", err)
	}
	if len(countries) != 2 {
		t.Fatalf("countries = %+v, want AR and BR only", countries)
	}
	for _, country := range countries {
		if country.Code != "AR" {
			continue
		}
		if country.Name != "アルゼンチン" || country.Alpha3Code != "ARG" || country.NumericCode != 32 || country.ContinentCode != "SA" ||
			*country.CurrencyCode != "ARS" || *country.CapitalCityID != buenosAires || *country.DefaultTimeZoneID != "America/Argentina/Buenos_Aires" {
			t.Fatalf("AR = %+v", country)
		}
	}
}

func TestIntegrationCountryRepositoryFallsBackToTheOwnName(t *testing.T) {
	t.Parallel()
	repository := postgres.NewCountryRepository(newReadyPool(t))

	countries, err := repository.CountriesByCode(context.Background(), noLocale, []string{"AR"})
	if err != nil || len(countries) != 1 || countries[0].Name != "Argentina" {
		t.Fatalf("AR without a locale = %+v, %v; want its own name", countries, err)
	}
}
