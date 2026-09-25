//go:build integration

package main

import (
	"context"
	"database/sql"
	"testing"
	"time"

	_ "github.com/jackc/pgx/v5/stdlib"
	"github.com/pressly/goose/v3"
	"github.com/testcontainers/testcontainers-go/modules/postgres"

	"github.com/MathiasHilgert/frappe-api/migrations"
)

// TestIntegrationMigrationsApplyOnAFreshDatabase proves migrations.FS's
// embedded SQL files actually apply, end to end, against a real Postgres
// database, using the same goose.Provider construction as run() in
// main.go (session locker plus WithAllowOutofOrder, since migration files
// are timestamp-versioned; see migrations/doc.go). It is not executed by
// "go vet -tags=integration ./..." or a plain "go build" (Docker is not
// available everywhere this module builds); it only runs under
// "task test:integration" / CI, which do have Docker.
func TestIntegrationMigrationsApplyOnAFreshDatabase(t *testing.T) {
	ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
	defer cancel()

	container, err := postgres.Run(ctx, "postgres:18.1",
		postgres.WithDatabase("frappe"),
		postgres.WithUsername("frappe_migration"),
		postgres.WithPassword("frappe_migration"),
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

	sqlDatabase, err := sql.Open("pgx", connectionString)
	if err != nil {
		t.Fatalf("open database: %v", err)
	}
	t.Cleanup(func() { _ = sqlDatabase.Close() })

	// The migration itself grants privileges to frappe_application (see
	// migrations/20260924000000_application_role_privileges.sql); that
	// role does not need to exist for the migration to apply, since
	// granting privileges to a role name is independent of whether a
	// session currently uses it, but the role must exist for the grant
	// statements themselves to succeed.
	if _, execErr := sqlDatabase.ExecContext(ctx, "CREATE ROLE frappe_application WITH LOGIN PASSWORD 'frappe_application' NOSUPERUSER NOBYPASSRLS"); execErr != nil {
		t.Fatalf("create frappe_application role: %v", execErr)
	}
	// migrations/20260925190341_outbox.sql grants the outbox relay role
	// its privileges by name, so it must exist too.
	if _, execErr := sqlDatabase.ExecContext(ctx, "CREATE ROLE frappe_outbox_relay WITH LOGIN PASSWORD 'frappe_outbox_relay' NOSUPERUSER NOBYPASSRLS"); execErr != nil {
		t.Fatalf("create frappe_outbox_relay role: %v", execErr)
	}

	provider, err := goose.NewProvider(goose.DialectPostgres, sqlDatabase, migrations.FS, goose.WithAllowOutofOrder(true))
	if err != nil {
		t.Fatalf("create migration provider: %v", err)
	}

	results, err := provider.Up(ctx)
	if err != nil {
		t.Fatalf("apply migrations: %v", err)
	}
	if len(results) == 0 {
		t.Fatal("Up applied zero migrations")
	}

	version, err := provider.GetDBVersion(ctx)
	if err != nil {
		t.Fatalf("get migration version: %v", err)
	}
	if version <= 0 {
		t.Fatalf("GetDBVersion = %d, want a positive applied version", version)
	}

	// Every Down section must reverse its Up cleanly: roll everything back
	// and apply it again.
	if _, err := provider.DownTo(ctx, 0); err != nil {
		t.Fatalf("roll back every migration: %v", err)
	}
	if _, err := provider.Up(ctx); err != nil {
		t.Fatalf("reapply migrations after rolling back: %v", err)
	}
}
