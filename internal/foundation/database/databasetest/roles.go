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
)

// bootstrapRoles creates the two application roles against the shared
// server, mirroring deployments/database/initialize.sql. It is
// idempotent: on a reused container (testcontainers.WithReuseByName),
// the roles may already exist from a previous "go test" run, so each
// statement uses a DO block that only creates the role when it is
// missing, instead of a plain CREATE ROLE that would fail on the second
// run.
func bootstrapRoles(ctx context.Context, address serverAddress) error {
	connection, err := sql.Open("pgx", superuserURL(address, superuserDatabase))
	if err != nil {
		return fmt.Errorf("databasetest: open superuser connection: %w", err)
	}
	defer func() { _ = connection.Close() }()

	statements := []string{
		createRoleIfMissing(migrationRoleUsername, migrationRolePassword, migrationRoleCapability),
		createRoleIfMissing(applicationRoleUsername, applicationRolePassword, applicationRoleCapability),
	}
	for _, statement := range statements {
		if _, execErr := connection.ExecContext(ctx, statement); execErr != nil {
			return fmt.Errorf("databasetest: bootstrap role: %w", execErr)
		}
	}

	return nil
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
