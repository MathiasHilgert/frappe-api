package migrations

import (
	"compress/gzip"
	"context"
	"database/sql"
	"embed"
	"errors"
	"fmt"
	"strings"

	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/stdlib"
)

// Data embeds the data files Go migrations load: the GeoNames snapshot
// written by cmd/geosnapshot (data/geo/*.tsv.gz, COPY text format, plus
// its manifest.json).
//
//go:embed data
var Data embed.FS

// GoMigration is a migration written in Go, for work a SQL file cannot do
// (such as streaming a compressed data file through COPY). It is plain
// data: this package never imports the migration tool, so each runner
// (cmd/migrate, the integration test template) registers these itself.
// Up and Down receive a database/sql handle opened with the pgx driver
// and manage their own transaction.
type GoMigration struct {
	Up   func(ctx context.Context, database *sql.DB) error
	Down func(ctx context.Context, database *sql.DB) error
	// Revision identifies the migration's code: bump it whenever Up or
	// Down changes, so the integration test template (keyed by a hash of
	// every migration) is rebuilt.
	Revision string
	Version  int64
}

// GoMigrations returns every Go migration, in version order.
func GoMigrations() []GoMigration {
	return []GoMigration{
		{Version: geoSeedVersion, Revision: "geo-seed-3", Up: seedGeo, Down: unseedGeo},
	}
}

// geoSeedVersion runs right after 20260928000000_geo.sql created the
// tables it fills.
const geoSeedVersion = 20260928000001

// geoTable is one snapshot file and the table and columns it loads into,
// in the column order cmd/geosnapshot writes.
type geoTable struct {
	name    string
	columns string
}

// geoTables lists the snapshot tables in load order: referenced first
// (the countries -> cities and countries -> time_zones foreign keys are
// deferred to the end of the seed transaction).
var geoTables = []geoTable{ //nolint:gochecknoglobals // constant load plan.
	{name: "places", columns: "id, kind, name"},
	{name: "countries", columns: "code, place_id, alpha3_code, numeric_code, continent_code, currency_code, capital_city_id, default_time_zone_id"},
	{name: "time_zones", columns: "id, country_code, january_offset_hours, july_offset_hours, raw_offset_hours"},
	{name: "subdivisions", columns: "place_id, country_code, iso_code, geonames_admin1_code"},
	{name: "cities", columns: "place_id, country_code, subdivision_id, ascii_name, latitude, longitude, population, feature_code, time_zone_id"},
	{name: "place_names", columns: "place_id, locale, name"},
}

// seedGeo loads the geo snapshot in one transaction: each file is
// streamed, still gzip-compressed in the binary, through COPY into a
// temporary staging table, then inserted with ON CONFLICT DO NOTHING, so
// running it again (for example when the transaction committed but the
// runner failed to record the version) changes nothing.
func seedGeo(ctx context.Context, database *sql.DB) error {
	return withPgxTransaction(ctx, database, func(transaction pgx.Tx) error {
		for _, entry := range geoTables {
			if err := loadGeoTable(ctx, transaction, entry); err != nil {
				return err
			}
		}
		return nil
	})
}

func loadGeoTable(ctx context.Context, transaction pgx.Tx, entry geoTable) error {
	file, err := Data.Open("data/geo/" + entry.name + ".tsv.gz")
	if err != nil {
		return fmt.Errorf("open %s snapshot: %w", entry.name, err)
	}
	defer func() { _ = file.Close() }()
	reader, err := gzip.NewReader(file)
	if err != nil {
		return fmt.Errorf("decompress %s snapshot: %w", entry.name, err)
	}

	staging := pgx.Identifier{"geo_seed_" + entry.name}.Sanitize()
	target := pgx.Identifier{entry.name}.Sanitize()
	createStaging := fmt.Sprintf("CREATE TEMPORARY TABLE %s ON COMMIT DROP AS SELECT %s FROM %s WITH NO DATA", staging, entry.columns, target)
	if _, err := transaction.Exec(ctx, createStaging); err != nil {
		return fmt.Errorf("stage %s: %w", entry.name, err)
	}
	copyStatement := fmt.Sprintf("COPY %s (%s) FROM STDIN", staging, entry.columns)
	if _, err := transaction.Conn().PgConn().CopyFrom(ctx, reader, copyStatement); err != nil {
		return fmt.Errorf("copy %s snapshot: %w", entry.name, err)
	}
	insert := fmt.Sprintf("INSERT INTO %s (%s) SELECT %s FROM %s ON CONFLICT DO NOTHING",
		target, entry.columns, entry.columns, staging)
	if _, err := transaction.Exec(ctx, insert); err != nil {
		return fmt.Errorf("insert %s: %w", entry.name, err)
	}
	return nil
}

// unseedGeo empties every geo table; 20260928000000_geo.sql's Down then
// drops them.
func unseedGeo(ctx context.Context, database *sql.DB) error {
	names := make([]string, 0, len(geoTables))
	for _, entry := range geoTables {
		names = append(names, pgx.Identifier{entry.name}.Sanitize())
	}
	_, err := database.ExecContext(ctx, "TRUNCATE "+strings.Join(names, ", "))
	if err != nil {
		return fmt.Errorf("truncate geo tables: %w", err)
	}
	return nil
}

// withPgxTransaction runs work in a pgx transaction on one connection of
// database, which must use the pgx database/sql driver: COPY FROM STDIN
// needs the native pgx connection.
func withPgxTransaction(ctx context.Context, database *sql.DB, work func(pgx.Tx) error) error {
	connection, err := database.Conn(ctx)
	if err != nil {
		return fmt.Errorf("acquire connection: %w", err)
	}
	defer func() { _ = connection.Close() }()

	return connection.Raw(func(driverConnection any) error {
		native, ok := driverConnection.(*stdlib.Conn)
		if !ok {
			return errors.New("geo seed needs the pgx database/sql driver")
		}
		return pgx.BeginFunc(ctx, native.Conn(), work)
	})
}
