//go:build integration

package databasetest

import (
	"context"
	"database/sql"
	"fmt"

	_ "github.com/jackc/pgx/v5/stdlib" // registers the "pgx" database/sql driver used below.
)

// migrationRoleUsername and applicationRoleUsername match the role names
// deployments/database/initialize.sql creates and the migration in
// migrations/20260924000000_application_role_privileges.sql grants
// privileges to by name. Reusing the exact same names here, instead of
// test-only aliases, is what lets that migration's "GRANT ... TO
// frappe_application" apply unchanged against the shared test container.
const (
	migrationRoleUsername   = "frappe_migration"
	migrationRolePassword   = "frappe_migration_test_only"
	migrationRoleCapability = "NOSUPERUSER CREATEROLE"

	applicationRoleUsername = "frappe_application"
	// applicationRolePassword and migrationRolePassword (above) are not
	// real credentials: they only ever reach a disposable,
	// container-local Postgres server started by this test helper, the
	// same way deployments/database/initialize.sql's development-only
	// passwords do.
	applicationRolePassword   = "frappe_application_test_only" //nolint:gosec // test-only container credential, never a real secret.
	applicationRoleCapability = "NOSUPERUSER NOBYPASSRLS"

	// outboxRelayRoleUsername is the role the outbox relay connects as
	// (see migrations/20260925190341_outbox.sql): it may only read,
	// update and delete outbox rows, across every tenant.
	outboxRelayRoleUsername   = "frappe_outbox_relay"
	outboxRelayRolePassword   = "frappe_outbox_relay_test_only" //nolint:gosec // test-only container credential, never a real secret.
	outboxRelayRoleCapability = "NOSUPERUSER NOBYPASSRLS"
)

// roleBootstrapLockKey is the pg_advisory_xact_lock key bootstrapRoles
// holds while it creates roles. Its value is arbitrary; it only has to be
// the same constant in every package test binary.
const roleBootstrapLockKey = 7_246_300_001

// bootstrapRoles creates the application roles against the shared
// server, mirroring deployments/database/initialize.sql.
//
// Every package test binary of one "go test" invocation attaches to the
// same container and calls bootstrapRoles concurrently, so the
// check-then-create in createRoleIfMissing alone would race. The
// statements therefore run in one transaction that first takes a
// transaction-scoped advisory lock, serializing the binaries; as a second
// line of defense, a duplicate_object or unique_violation error (see
// isAlreadyExistsError), which only means another binary created a role
// first, triggers one retry instead of failing.
func bootstrapRoles(ctx context.Context, address serverAddress) error {
	connection, err := sql.Open("pgx", superuserURL(address, superuserDatabase))
	if err != nil {
		return fmt.Errorf("databasetest: open superuser connection: %w", err)
	}
	defer func() { _ = connection.Close() }()

	err = createRolesLocked(ctx, connection)
	if isAlreadyExistsError(err) {
		// The failed transaction rolled back every statement, so run
		// them once more: the roles another binary created are now
		// visible and skipped, and the others are still created.
		err = createRolesLocked(ctx, connection)
	}
	if err != nil {
		return fmt.Errorf("databasetest: bootstrap roles: %w", err)
	}

	return nil
}

// createRolesLocked runs every role statement in one transaction holding
// roleBootstrapLockKey, and commits it.
func createRolesLocked(ctx context.Context, connection *sql.DB) error {
	transaction, err := connection.BeginTx(ctx, nil)
	if err != nil {
		return err
	}
	defer func() { _ = transaction.Rollback() }()

	statements := []string{
		fmt.Sprintf("SELECT pg_advisory_xact_lock(%d)", roleBootstrapLockKey),
		createRoleIfMissing(migrationRoleUsername, migrationRolePassword, migrationRoleCapability),
		createRoleIfMissing(applicationRoleUsername, applicationRolePassword, applicationRoleCapability),
		createRoleIfMissing(outboxRelayRoleUsername, outboxRelayRolePassword, outboxRelayRoleCapability),
	}
	for _, statement := range statements {
		if _, execErr := transaction.ExecContext(ctx, statement); execErr != nil {
			return execErr
		}
	}

	return transaction.Commit()
}

// createRoleIfMissing returns a DO block that creates a LOGIN role with
// the given password and capabilities only if no role with that name
// already exists.
func createRoleIfMissing(username, password, capabilities string) string {
	return fmt.Sprintf(
		`DO $$ BEGIN
			IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = %[1]s) THEN
				EXECUTE format('CREATE ROLE %%I WITH LOGIN PASSWORD %%L %[3]s', %[1]s, %[2]s);
			END IF;
		END $$;`,
		quoteLiteral(username), quoteLiteral(password), capabilities,
	)
}

// quoteLiteral wraps value in single quotes for embedding as a SQL
// string literal. The role names and passwords this package generates
// are all fixed, package-internal constants, never user input, so a
// simple quote is sufficient and this must never be used with untrusted
// data.
func quoteLiteral(value string) string {
	return "'" + value + "'"
}

// superuserURL builds a Postgres connection string for the container's
// own superuser, connecting to the given database.
func superuserURL(address serverAddress, database string) string {
	return fmt.Sprintf(
		"postgres://%s:%s@%s:%s/%s?sslmode=disable",
		superuserUsername, superuserPassword, address.host, address.port, database,
	)
}
