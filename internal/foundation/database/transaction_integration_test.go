//go:build integration

package database_test

import (
	"context"
	"errors"
	"fmt"
	"testing"
	"time"

	"github.com/jackc/pgx/v5"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/database"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/database/databasetest"
)

func TestIntegrationWithinTransactionAppliesSettingsOnlyInsideTheTransaction(t *testing.T) {
	t.Parallel()

	pool := databasetest.NewOwner(t)

	ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
	defer cancel()

	var insideValue string
	err := database.WithinTransaction(ctx, pool, database.TransactionSettings{
		"application.tenant": "acme",
	}, func(ctx context.Context, transaction pgx.Tx) error {
		return transaction.QueryRow(ctx, "SELECT current_setting('application.tenant', true)").Scan(&insideValue)
	})
	if err != nil {
		t.Fatalf("WithinTransaction returned unexpected error: %v", err)
	}
	if insideValue != "acme" {
		t.Fatalf("current_setting inside the transaction = %q, want %q", insideValue, "acme")
	}

	// A second, unrelated query on the pool (a new connection or a
	// reused one after the transaction released it) must not see the
	// setting: it was transaction-local, not session-level.
	var afterValue string
	if err := pool.QueryRow(ctx, "SELECT current_setting('application.tenant', true)").Scan(&afterValue); err != nil {
		t.Fatalf("query after commit returned unexpected error: %v", err)
	}
	if afterValue != "" {
		t.Fatalf("current_setting after commit = %q, want empty (setting must not leak across the pool)", afterValue)
	}
}

func TestIntegrationWithinTransactionRollsBackOnWorkError(t *testing.T) {
	t.Parallel()

	pool := databasetest.NewOwner(t)

	ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
	defer cancel()

	if _, createErr := pool.Exec(ctx, "CREATE TABLE rollback_probe (value TEXT NOT NULL)"); createErr != nil {
		t.Fatalf("create table: %v", createErr)
	}

	wantErr := errors.New("boom")
	err := database.WithinTransaction(ctx, pool, nil, func(ctx context.Context, transaction pgx.Tx) error {
		if _, insertErr := transaction.Exec(ctx, "INSERT INTO rollback_probe (value) VALUES ('should not persist')"); insertErr != nil {
			return insertErr
		}
		return wantErr
	})
	if !errors.Is(err, wantErr) {
		t.Fatalf("WithinTransaction error = %v, want %v", err, wantErr)
	}

	var rowCount int
	if err := pool.QueryRow(ctx, "SELECT count(*) FROM rollback_probe").Scan(&rowCount); err != nil {
		t.Fatalf("count rows: %v", err)
	}
	if rowCount != 0 {
		t.Fatalf("rollback_probe has %d rows, want 0 (work's insert must have been rolled back)", rowCount)
	}
}

// TestIntegrationNestedWithinTransactionJoinsTheOuterTransaction proves a
// nested WithinTransaction runs inside the outer transaction through a
// savepoint: a failed nested unit rolls back only its own writes, and a
// failed outer unit rolls back the nested unit's released writes too.
// It uses a pool of one connection, so a nested call that opened a second
// connection instead of joining would deadlock and hit the ctx timeout.
func TestIntegrationNestedWithinTransactionJoinsTheOuterTransaction(t *testing.T) {
	connectionString := startPostgresContainer(t, "frappe_superuser", "frappe_superuser")

	ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
	defer cancel()

	pool, err := database.Up(ctx, database.Settings{URL: connectionString, MaxConnections: 1})
	if err != nil {
		t.Fatalf("Up returned unexpected error: %v", err)
	}
	t.Cleanup(func() { _ = database.Down(context.Background(), pool) })

	if _, createErr := pool.Exec(ctx, "CREATE TABLE nested_probe (value TEXT NOT NULL)"); createErr != nil {
		t.Fatalf("create table: %v", createErr)
	}

	insert := func(value string) func(context.Context, pgx.Tx) error {
		return func(ctx context.Context, transaction pgx.Tx) error {
			_, insertErr := transaction.Exec(ctx, "INSERT INTO nested_probe (value) VALUES ($1)", value)
			return insertErr
		}
	}

	nestedErr := errors.New("nested failure")
	outerErr := errors.New("outer failure")
	err = database.WithinTransaction(ctx, pool, nil, func(ctx context.Context, transaction pgx.Tx) error {
		if insertErr := insert("outer")(ctx, transaction); insertErr != nil {
			return insertErr
		}
		if releasedErr := database.WithinTransaction(ctx, pool, nil, insert("released")); releasedErr != nil {
			return releasedErr
		}
		failedErr := database.WithinTransaction(ctx, pool, nil, func(ctx context.Context, transaction pgx.Tx) error {
			if insertErr := insert("rolled back savepoint")(ctx, transaction); insertErr != nil {
				return insertErr
			}
			return nestedErr
		})
		if !errors.Is(failedErr, nestedErr) {
			return fmt.Errorf("nested error = %w, want %w", failedErr, nestedErr)
		}

		var visible int
		if countErr := transaction.QueryRow(ctx, "SELECT count(*) FROM nested_probe").Scan(&visible); countErr != nil {
			return countErr
		}
		if visible != 2 {
			return fmt.Errorf("rows visible inside the outer transaction = %d, want 2", visible)
		}
		return outerErr
	})
	if !errors.Is(err, outerErr) {
		t.Fatalf("WithinTransaction error = %v, want %v", err, outerErr)
	}

	var rowCount int
	if err := pool.QueryRow(ctx, "SELECT count(*) FROM nested_probe").Scan(&rowCount); err != nil {
		t.Fatalf("count rows: %v", err)
	}
	if rowCount != 0 {
		t.Fatalf("nested_probe has %d rows, want 0 (the outer rollback must undo the released savepoint)", rowCount)
	}
}

// TestIntegrationRowLevelSecurityIsEnforcedForANonSuperuserRole proves Row
// Level Security actually restricts a NOSUPERUSER, NOBYPASSRLS role on a
// table created with FORCE ROW LEVEL SECURITY and a policy driven by the
// same current_setting WithinTransaction populates, end to end. It uses
// NewOwner (the schema-owning role) to create the scratch table and
// policy, and New (the application role) to prove the restriction, the
// same split of privileges the running API and cmd/migrate use.
func TestIntegrationRowLevelSecurityIsEnforcedForANonSuperuserRole(t *testing.T) {
	t.Parallel()

	ownerPool, applicationPool := databasetest.NewWithOwner(t)

	ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
	defer cancel()

	setupStatements := []string{
		"CREATE TABLE rls_probe (tenant TEXT NOT NULL, value TEXT NOT NULL)",
		"GRANT SELECT, INSERT ON rls_probe TO frappe_application",
		"ALTER TABLE rls_probe ENABLE ROW LEVEL SECURITY",
		"ALTER TABLE rls_probe FORCE ROW LEVEL SECURITY",
		`CREATE POLICY rls_probe_tenant_isolation ON rls_probe
			USING (tenant = current_setting('application.tenant', true))
			WITH CHECK (tenant = current_setting('application.tenant', true))`,
		"INSERT INTO rls_probe (tenant, value) VALUES ('acme', 'acme-row'), ('globex', 'globex-row')",
	}
	for _, statement := range setupStatements {
		if _, setupErr := ownerPool.Exec(ctx, statement); setupErr != nil {
			t.Fatalf("setup statement %q: %v", statement, setupErr)
		}
	}

	var visibleValues []string
	err := database.WithinTransaction(ctx, applicationPool, database.TransactionSettings{
		"application.tenant": "acme",
	}, func(ctx context.Context, transaction pgx.Tx) error {
		rows, queryErr := transaction.Query(ctx, "SELECT value FROM rls_probe ORDER BY value")
		if queryErr != nil {
			return queryErr
		}
		defer rows.Close()

		for rows.Next() {
			var value string
			if scanErr := rows.Scan(&value); scanErr != nil {
				return scanErr
			}
			visibleValues = append(visibleValues, value)
		}
		return rows.Err()
	})
	if err != nil {
		t.Fatalf("WithinTransaction returned unexpected error: %v", err)
	}

	if len(visibleValues) != 1 || visibleValues[0] != "acme-row" {
		t.Fatalf("visible rows for tenant acme = %v, want [acme-row] (Row Level Security must hide the globex row)", visibleValues)
	}
}
