//go:build integration

package migrations_test

import (
	"context"
	"errors"
	"strings"
	"testing"

	"github.com/jackc/pgx/v5/pgconn"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/database/databasetest"
)

// insufficientPrivilege is the SQLSTATE Postgres reports when a role lacks
// a table privilege.
const insufficientPrivilege = "42501"

func TestIntegrationGeoSeedLoadsTheSnapshot(t *testing.T) {
	t.Parallel()
	pool := databasetest.New(t)
	ctx := context.Background()

	var countryName, continent, currency string
	if err := pool.QueryRow(ctx,
		"SELECT name, continent_code, currency_code FROM countries WHERE code = 'AR'",
	).Scan(&countryName, &continent, &currency); err != nil {
		t.Fatalf("query Argentina: %v", err)
	}
	if countryName != "Argentina" || continent != "SA" || currency != "ARS" {
		t.Errorf("Argentina = %q %q %q", countryName, continent, currency)
	}

	var provinceCode, provinceName string
	if err := pool.QueryRow(ctx,
		"SELECT code, name FROM subdivisions WHERE id = 3860255 AND country_code = 'AR'",
	).Scan(&provinceCode, &provinceName); err != nil {
		t.Fatalf("query Cordoba province: %v", err)
	}
	if provinceCode != "05" || provinceName != "Cordoba" {
		t.Errorf("Cordoba province = %q %q", provinceCode, provinceName)
	}

	var cityName, timeZone string
	var subdivisionID, population int64
	if err := pool.QueryRow(ctx,
		"SELECT name, subdivision_id, population, time_zone_id FROM cities WHERE id = 3832734",
	).Scan(&cityName, &subdivisionID, &population, &timeZone); err != nil {
		t.Fatalf("query Villa General Belgrano: %v", err)
	}
	if cityName != "Villa General Belgrano" || subdivisionID != 3860255 || population <= 500 ||
		timeZone != "America/Argentina/Cordoba" {
		t.Errorf("Villa General Belgrano = %q %d %d %q", cityName, subdivisionID, population, timeZone)
	}
}

func TestIntegrationGeoSeedLocalizesNamesWithFallback(t *testing.T) {
	t.Parallel()
	pool := databasetest.New(t)

	// A locale without a row falls back to the entity's own name.
	query := `SELECT coalesce(country_names.name, countries.name)
		FROM countries
		LEFT JOIN country_names ON country_names.country_code = countries.code AND country_names.locale = $1
		WHERE countries.code = 'AR'`
	want := map[string]string{"en": "Argentina", "pt-BR": "Argentina", "ja": "アルゼンチン", "de": "Argentinien"}
	for locale, name := range want {
		var got string
		if err := pool.QueryRow(context.Background(), query, locale).Scan(&got); err != nil {
			t.Fatalf("query Argentina in %s: %v", locale, err)
		}
		if got != name {
			t.Errorf("Argentina in %s = %q, want %q", locale, got, name)
		}
	}

	var japanese string
	if err := pool.QueryRow(context.Background(),
		"SELECT name FROM subdivision_names WHERE subdivision_id = 3860255 AND locale = 'ja'",
	).Scan(&japanese); err != nil {
		t.Fatalf("query Cordoba province in ja: %v", err)
	}
	if japanese != "コルドバ州" {
		t.Errorf("Cordoba province in ja = %q", japanese)
	}
}

func TestIntegrationGeoSearchIgnoresAccentsAndCase(t *testing.T) {
	t.Parallel()
	pool := databasetest.New(t)

	var name string
	if err := pool.QueryRow(context.Background(), `
		SELECT name FROM cities
		WHERE geo_search_key(name) % geo_search_key($1) AND country_code = 'AR'
		ORDER BY similarity(geo_search_key(name), geo_search_key($1)) DESC, population DESC
		LIMIT 1`, "cordoba",
	).Scan(&name); err != nil {
		t.Fatalf("search cordoba: %v", err)
	}
	if name != "Córdoba" {
		t.Errorf("best match for cordoba = %q, want Córdoba", name)
	}

	var plan string
	rows, err := pool.Query(context.Background(),
		"EXPLAIN SELECT id FROM cities WHERE geo_search_key(name) % geo_search_key('cordoba')")
	if err != nil {
		t.Fatalf("explain search: %v", err)
	}
	for rows.Next() {
		var line string
		if err := rows.Scan(&line); err != nil {
			t.Fatalf("read plan: %v", err)
		}
		plan += line + "\n"
	}
	rows.Close()
	if !strings.Contains(plan, "cities_name_search") {
		t.Errorf("search does not use the trigram index:\n%s", plan)
	}
}

func TestIntegrationGeoTablesAreReadOnlyForTheApplication(t *testing.T) {
	t.Parallel()
	pool := databasetest.New(t)
	ctx := context.Background()

	statements := []string{
		"INSERT INTO countries (code, alpha3_code, numeric_code, geonames_id, name, continent_code) VALUES ('ZZ', 'ZZZ', 999, 1, 'Nowhere', 'EU')",
		"UPDATE cities SET population = 0 WHERE id = 3832734",
		"DELETE FROM city_names",
		"TRUNCATE time_zones CASCADE",
	}
	for _, statement := range statements {
		_, err := pool.Exec(ctx, statement)
		var postgresError *pgconn.PgError
		if !errors.As(err, &postgresError) || postgresError.Code != insufficientPrivilege {
			t.Errorf("%s: got %v, want insufficient privilege", statement, err)
		}
	}
}
