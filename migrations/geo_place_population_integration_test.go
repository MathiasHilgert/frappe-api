//go:build integration

package migrations_test

import (
	"context"
	"testing"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/database/databasetest"
)

func TestIntegrationGeoPopulationsSumTheirCities(t *testing.T) {
	t.Parallel()
	pool := databasetest.New(t)

	var country, subdivision, subdivisionCities, countryCities int64
	if err := pool.QueryRow(context.Background(), `
		SELECT countries.population, subdivisions.population,
			(SELECT sum(population) FROM cities WHERE subdivision_id = subdivisions.place_id),
			(SELECT sum(population) FROM cities WHERE country_code = countries.code)
		FROM subdivisions JOIN countries ON countries.code = subdivisions.country_code
		WHERE subdivisions.iso_code = 'AR-X'`,
	).Scan(&country, &subdivision, &subdivisionCities, &countryCities); err != nil {
		t.Fatalf("query populations: %v", err)
	}
	if subdivision != subdivisionCities || country != countryCities || subdivision == 0 || country <= subdivision {
		t.Errorf("AR population %d (cities %d), AR-X %d (cities %d)", country, countryCities, subdivision, subdivisionCities)
	}
}
