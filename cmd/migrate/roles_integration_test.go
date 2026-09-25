//go:build integration

package main

import (
	"context"
	"database/sql"
	"net/url"
	"os"
	"path/filepath"
	"testing"
	"time"

	_ "github.com/jackc/pgx/v5/stdlib"
	"github.com/pressly/goose/v3"
	"github.com/pressly/goose/v3/lock"
	"github.com/testcontainers/testcontainers-go/modules/postgres"

	"github.com/MathiasHilgert/frappe-api/migrations"
)

// TestIntegrationMigrationsRunAsTheNonSuperuserMigrationRole bootstraps a
// fresh Postgres 18 database with deployments/database/initialize.sql (as
// the superuser, exactly like the postgres image's initdb hook does in
// compose.yaml), then applies every migration connected as the
// NOSUPERUSER frappe_migration role, creates a table as that role, and
// checks that the NOSUPERUSER frappe_application role can use it. It
// guards against Postgres 15+'s public schema ownership change, under
// which a non-owner migration role can neither CREATE in public nor
// GRANT USAGE on it. It only runs under "task test:integration" / CI,
// which have Docker.
func TestIntegrationMigrationsRunAsTheNonSuperuserMigrationRole(t *testing.T) {
	ctx, cancel := context.WithTimeout(context.Background(), 60*time.Second)
	defer cancel()

	superuserURL := startDatabase(ctx, t)

	initializeSQL, err := os.ReadFile(filepath.Join("..", "..", "deployments", "database", "initialize.sql"))
	if err != nil {
		t.Fatalf("read initialize.sql: %v", err)
	}
	superuser := openDatabase(t, superuserURL)
	if _, execErr := superuser.ExecContext(ctx, string(initializeSQL)); execErr != nil {
		t.Fatalf("run initialize.sql as the superuser: %v", execErr)
	}

	migration := openDatabase(t, withCredentials(t, superuserURL, "frappe_migration", "frappe_migration_development_only"))
	applyMigrations(ctx, t, migration)

	migrationStatements := []string{
		"CREATE TABLE role_probe (identifier BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY, value TEXT NOT NULL)",
	}
	for _, statement := range migrationStatements {
		if _, execErr := migration.ExecContext(ctx, statement); execErr != nil {
			t.Fatalf("run %q as frappe_migration: %v", statement, execErr)
		}
	}

	application := openDatabase(t, withCredentials(t, superuserURL, "frappe_application", "frappe_application_development_only"))
	if _, execErr := application.ExecContext(ctx, "INSERT INTO role_probe (value) VALUES ('written by the application role')"); execErr != nil {
		t.Fatalf("insert as frappe_application: %v", execErr)
	}
	var rowCount int
	if queryErr := application.QueryRowContext(ctx, "SELECT count(*) FROM role_probe").Scan(&rowCount); queryErr != nil {
		t.Fatalf("select as frappe_application: %v", queryErr)
	}
	if rowCount != 1 {
		t.Fatalf("role_probe has %d rows, want 1", rowCount)
	}
	if _, execErr := application.ExecContext(ctx, "CREATE TABLE application_must_not_create (value TEXT)"); execErr == nil {
		t.Fatal("frappe_application created a table in public; only the migration role may")
	}
}

// startDatabase starts a disposable Postgres 18 container whose superuser
// is "postgres" and whose database is "frappe", matching compose.yaml, and
// returns the superuser's connection string.
func startDatabase(ctx context.Context, t *testing.T) string {
	t.Helper()

	container, err := postgres.Run(ctx, "postgres:18.1",
		postgres.WithDatabase("frappe"),
		postgres.WithUsername("postgres"),
		postgres.WithPassword("postgres"),
		postgres.BasicWaitStrategies(),
	)
	if err != nil {
		t.Fatalf("start postgres container: %v", err)
	}
	t.Cleanup(func() {
		if terminateErr := container.Terminate(context.Background()); terminateErr != nil {
			t.Logf("terminate postgres container: %v", terminateErr)
		}
	})

	connectionString, err := container.ConnectionString(ctx, "sslmode=disable")
	if err != nil {
		t.Fatalf("get postgres connection string: %v", err)
	}
	return connectionString
}

// openDatabase opens connectionString with the pgx database/sql driver
// and closes it when the test ends.
func openDatabase(t *testing.T, connectionString string) *sql.DB {
	t.Helper()

	opened, err := sql.Open("pgx", connectionString)
	if err != nil {
		t.Fatalf("open database: %v", err)
	}
	t.Cleanup(func() { _ = opened.Close() })
	return opened
}

// withCredentials returns connectionString with its user and password
// replaced, keeping host, port, database and query parameters.
func withCredentials(t *testing.T, connectionString, username, password string) string {
	t.Helper()

	parsed, err := url.Parse(connectionString)
	if err != nil {
		t.Fatalf("parse connection string: %v", err)
	}
	parsed.User = url.UserPassword(username, password)
	return parsed.String()
}

// applyMigrations applies every embedded migration on migration, built
// the same way run() in main.go builds its goose.Provider.
func applyMigrations(ctx context.Context, t *testing.T, migration *sql.DB) {
	t.Helper()

	sessionLocker, err := lock.NewPostgresSessionLocker()
	if err != nil {
		t.Fatalf("create migration session locker: %v", err)
	}
	provider, err := goose.NewProvider(goose.DialectPostgres, migration, migrations.FS,
		goose.WithSessionLocker(sessionLocker),
		goose.WithAllowOutofOrder(true),
		goMigrations(),
	)
	if err != nil {
		t.Fatalf("create migration provider: %v", err)
	}
	if _, err := provider.Up(ctx); err != nil {
		t.Fatalf("apply migrations as frappe_migration: %v", err)
	}
}
