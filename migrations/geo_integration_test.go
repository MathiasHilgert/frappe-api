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

func TestIntegrationGeoSeedResolvesSubdivisionByISOCode(t *testing.T) {
	t.Parallel()
	pool := databasetest.New(t)
	ctx := context.Background()

	var placeID int64
	var name, kind, admin1Code string
	if err := pool.QueryRow(ctx, `
		SELECT places.id, places.name, places.kind, subdivisions.geonames_admin1_code
		FROM subdivisions JOIN places ON places.id = subdivisions.place_id
		WHERE subdivisions.iso_code = 'AR-X'`,
	).Scan(&placeID, &name, &kind, &admin1Code); err != nil {
		t.Fatalf("query AR-X: %v", err)
	}
	if placeID != 3860255 || name != "Córdoba" || kind != "subdivision" || admin1Code != "05" {
		t.Errorf("AR-X = %d %q %q %q", placeID, name, kind, admin1Code)
	}

	// CLDR names; en is the place's own name, so it has no row.
	query := `SELECT coalesce(place_names.name, places.name)
		FROM places LEFT JOIN place_names ON place_names.place_id = places.id AND place_names.locale = $1
		WHERE places.id = 3860255`
	for locale, want := range map[string]string{"en": "Córdoba", "pt-BR": "Córdova (província da Argentina)", "ja": "コルドバ州"} {
		var got string
		if err := pool.QueryRow(ctx, query, locale).Scan(&got); err != nil {
			t.Fatalf("query AR-X in %s: %v", locale, err)
		}
		if got != want {
			t.Errorf("AR-X in %s = %q, want %q", locale, got, want)
		}
	}
}

func TestIntegrationGeoSeedLinksCountryCapitalAndDefaultTimeZone(t *testing.T) {
	t.Parallel()
	pool := databasetest.New(t)

	var countryName, continent, currency, capital, timeZone string
	if err := pool.QueryRow(context.Background(), `
		SELECT country.name, countries.continent_code, countries.currency_code, capital.name, countries.default_time_zone_id
		FROM countries
		JOIN places country ON country.id = countries.place_id
		JOIN places capital ON capital.id = countries.capital_city_id
		WHERE countries.code = 'AR'`,
	).Scan(&countryName, &continent, &currency, &capital, &timeZone); err != nil {
		t.Fatalf("query Argentina: %v", err)
	}
	if countryName != "Argentina" || continent != "SA" || currency != "ARS" ||
		capital != "Buenos Aires" || timeZone != "America/Argentina/Buenos_Aires" {
		t.Errorf("Argentina = %q %q %q capital %q time zone %q", countryName, continent, currency, capital, timeZone)
	}
}

func TestIntegrationGeoSeedLoadsCities(t *testing.T) {
	t.Parallel()
	pool := databasetest.New(t)

	var name, subdivisionISOCode, timeZone string
	var population int64
	if err := pool.QueryRow(context.Background(), `
		SELECT places.name, subdivisions.iso_code, cities.population, cities.time_zone_id
		FROM cities
		JOIN places ON places.id = cities.place_id
		JOIN subdivisions ON subdivisions.place_id = cities.subdivision_id
		WHERE cities.place_id = 3832734`,
	).Scan(&name, &subdivisionISOCode, &population, &timeZone); err != nil {
		t.Fatalf("query Villa General Belgrano: %v", err)
	}
	if name != "Villa General Belgrano" || subdivisionISOCode != "AR-X" || population <= 500 ||
		timeZone != "America/Argentina/Cordoba" {
		t.Errorf("Villa General Belgrano = %q %q %d %q", name, subdivisionISOCode, population, timeZone)
	}
}

// unifiedSearch matches own and localized names of every kind, ignoring
// accents and case, best similarity first, then population.
const unifiedSearch = `
	WITH matches AS (
		SELECT id AS place_id, similarity(search_key, geo_search_key($1)) AS score
		FROM places WHERE search_key % geo_search_key($1)
		UNION ALL
		SELECT place_id, similarity(search_key, geo_search_key($1))
		FROM place_names WHERE search_key % geo_search_key($1)
	)
	SELECT places.id, places.kind, places.name
	FROM (SELECT place_id, max(score) AS score FROM matches GROUP BY place_id) best
	JOIN places ON places.id = best.place_id
	LEFT JOIN cities ON cities.place_id = places.id
	ORDER BY best.score DESC, coalesce(cities.population, 0) DESC, places.id
	LIMIT 10`

func TestIntegrationGeoUnifiedSearchFindsEveryKindIgnoringAccents(t *testing.T) {
	t.Parallel()
	pool := databasetest.New(t)

	rows, err := pool.Query(context.Background(), unifiedSearch, "cordoba")
	if err != nil {
		t.Fatalf("search cordoba: %v", err)
	}
	defer rows.Close()
	var results []string
	kinds := map[string]bool{}
	for rows.Next() {
		var identifier int64
		var kind, name string
		if err := rows.Scan(&identifier, &kind, &name); err != nil {
			t.Fatalf("read result: %v", err)
		}
		results = append(results, kind+":"+name)
		kinds[kind] = true
	}
	if err := rows.Err(); err != nil {
		t.Fatalf("search cordoba: %v", err)
	}
	// Exact matches rank first; among them, the most populous city first.
	if len(results) < 2 || results[0] != "city:Córdoba" || !kinds["subdivision"] {
		t.Errorf("search cordoba = %v, want the city Córdoba first and the province among the results", results)
	}
	if !strings.Contains(strings.Join(results, ","), "subdivision:Córdoba") {
		t.Errorf("search cordoba = %v, want subdivision:Córdoba", results)
	}
}

func TestIntegrationGeoSearchUsesTheTrigramIndexes(t *testing.T) {
	t.Parallel()
	pool := databasetest.New(t)
	ctx := context.Background()

	// Tiny tables in a fresh database could be scanned sequentially; the
	// point is that the indexes are usable by the search expression.
	if _, err := pool.Exec(ctx, "SET enable_seqscan = off"); err != nil {
		t.Fatalf("disable sequential scans: %v", err)
	}
	for table, index := range map[string]string{"places": "places_search_key", "place_names": "place_names_search_key"} {
		var plan strings.Builder
		rows, err := pool.Query(ctx, "EXPLAIN SELECT 1 FROM "+table+" WHERE search_key % geo_search_key('cordoba')")
		if err != nil {
			t.Fatalf("explain %s: %v", table, err)
		}
		for rows.Next() {
			var line string
			if err := rows.Scan(&line); err != nil {
				t.Fatalf("read plan: %v", err)
			}
			plan.WriteString(line + "\n")
		}
		rows.Close()
		if !strings.Contains(plan.String(), index) {
			t.Errorf("%s search does not use %s:\n%s", table, index, plan.String())
		}
	}
}

func TestIntegrationGeoTablesAreReadOnlyForTheApplication(t *testing.T) {
	t.Parallel()
	pool := databasetest.New(t)
	ctx := context.Background()

	var count int
	if err := pool.QueryRow(ctx, "SELECT count(*) FROM places").Scan(&count); err != nil || count == 0 {
		t.Fatalf("select places: %d, %v", count, err)
	}
	statements := []string{
		"INSERT INTO places (id, kind, name) VALUES (1, 'city', 'Nowhere')",
		"UPDATE cities SET population = 0 WHERE place_id = 3832734",
		"UPDATE countries SET capital_city_id = NULL",
		"DELETE FROM place_names",
		"DELETE FROM subdivisions",
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
