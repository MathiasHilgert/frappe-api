//go:build integration

package databasetest

import (
	"context"
	"fmt"
	"testing"
	"time"

	"github.com/jackc/pgx/v5/pgxpool"
	"github.com/peterldowns/pgtestdb"
	"github.com/peterldowns/pgtestdb/migrators/goosemigrator"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/database"
	"github.com/MathiasHilgert/frappe-api/migrations"
)

// databaseUpTimeout bounds how long building the pgxpool.Pool for a
// freshly cloned test database (including database.Up's connect-and-
// ping) may take.
const databaseUpTimeout = 30 * time.Second

// testPoolMaxConnections and testPoolMinConnections size every pool this
// package returns. The container allows max_connections=500 (see
// container.go); with 4 connections per pool, and at most two pools per
// test (NewWithOwner), roughly 60 tests can hold full pools concurrently
// across all package binaries, leaving headroom for pgtestdb's own
// superuser connections. Production defaults (10 max) would exhaust the
// server at a quarter of that.
//
// testPoolMinConnections is 0 so idle test pools hold no connections.
// Note: database.Settings on this branch still replaces a zero
// MinConnections with DefaultMinConnections (2); once the fix making 0 a
// valid, explicit value lands, the pools hold no idle connections.
const (
	testPoolMaxConnections = int32(4)
	testPoolMinConnections = int32(0)
)

// New returns a *pgxpool.Pool, connected to a fresh, isolated database
// as the frappe_application role (see internal/foundation/database/doc.
// go for the two-role model), ready for use by a parallel integration
// test.
//
// The database is a clone of an already-migrated template, never
// migrated directly by this call, and Row Level Security behaves exactly
// as it does in production because the connecting role is
// NOSUPERUSER NOBYPASSRLS, the same as frappe_application in
// deployments/database/initialize.sql.
//
// New registers a t.Cleanup that closes the returned pool. On a passing
// test, pgtestdb additionally drops the database; on a failing test, it
// is left in place (and its connection string logged via t.Logf) for
// manual inspection.
func New(t *testing.T) *pgxpool.Pool {
	t.Helper()
	cloned := newClonedDatabase(t)
	return poolForURL(t, roleURL(cloned, applicationRoleUsername, applicationRolePassword))
}

// NewOwner returns a *pgxpool.Pool, connected to a fresh, isolated
// database as the frappe_migration role, the same schema-owning role
// cmd/migrate uses. Use it only when a test needs privileges
// frappe_application does not have, for example creating a scratch
// table with its own FORCE ROW LEVEL SECURITY policy to probe against;
// prefer New for everything else, so tests exercise the same restricted
// role the running API does.
func NewOwner(t *testing.T) *pgxpool.Pool {
	t.Helper()
	cloned := newClonedDatabase(t)
	return poolForURL(t, cloned.URL())
}

// NewWithOwner returns two *pgxpool.Pool values connected to the SAME
// fresh, isolated database: one as frappe_migration (ownerPool) and one
// as frappe_application (applicationPool). Use it for a test that must
// set schema up as the owner (a scratch table, a Row Level Security
// policy) and then prove behavior through the restricted application
// role against that exact same data, such as a Row Level Security
// enforcement test. New and NewOwner each provision their own separate
// database and must not be mixed for that purpose.
func NewWithOwner(t *testing.T) (ownerPool, applicationPool *pgxpool.Pool) {
	t.Helper()
	cloned := newClonedDatabase(t)
	ownerPool = poolForURL(t, cloned.URL())
	applicationPool = poolForURL(t, roleURL(cloned, applicationRoleUsername, applicationRolePassword))
	return ownerPool, applicationPool
}

// newClonedDatabase clones the migrated template into a fresh, isolated
// database and returns its connection details, still expressed for the
// frappe_migration role: migrations always apply as frappe_migration in
// production (see cmd/migrate/main.go), so that is the only role
// pgtestdb ever migrates or clones as. New and NewWithOwner then derive
// a frappe_application connection string to that same database with
// roleURL, instead of asking pgtestdb to clone (and migrate) a second,
// separate database per role.
func newClonedDatabase(t *testing.T) *pgtestdb.Config {
	t.Helper()

	address, err := server(context.Background())
	if err != nil {
		t.Fatalf("databasetest: prepare shared server: %v", err)
	}

	role := migrationRole()
	configuration := pgtestdb.Config{
		DriverName: "pgx",
		Host:       address.host,
		Port:       address.port,
		User:       superuserUsername,
		Password:   superuserPassword,
		Database:   superuserDatabase,
		Options:    "sslmode=disable",
		TestRole:   &role,
	}

	migrator := goosemigrator.New(".", goosemigrator.WithFS(migrations.FS))

	// pgtestdb.Custom migrates the template (once per process, cached by
	// migrator.Hash), clones it into a fresh database, closes its own
	// connection to that database and hands back the connection details
	// instead, so this package can build every pgxpool.Pool through the
	// same database.Up every non-test caller uses.
	return pgtestdb.Custom(t, configuration, migrator)
}

// poolForURL builds a pgxpool.Pool for connectionURL the same way the
// application itself builds one (database.Up), so tests observe the
// same pool behavior (tracing, pool limits, ping-on-connect) as the
// running API, and registers a t.Cleanup that closes it.
func poolForURL(t *testing.T, connectionURL string) *pgxpool.Pool {
	t.Helper()

	ctx, cancel := context.WithTimeout(context.Background(), databaseUpTimeout)
	defer cancel()

	pool, err := database.Up(ctx, database.Settings{
		URL:            connectionURL,
		MaxConnections: testPoolMaxConnections,
		MinConnections: testPoolMinConnections,
	})
	if err != nil {
		t.Fatalf("databasetest: build pool: %v", err)
	}
	t.Cleanup(pool.Close)

	return pool
}

// roleURL returns a Postgres connection string for username/password,
// keeping the host, port, database and options that cloned already
// resolved.
func roleURL(cloned *pgtestdb.Config, username, password string) string {
	return fmt.Sprintf(
		"postgres://%s:%s@%s:%s/%s?%s",
		username, password, cloned.Host, cloned.Port, cloned.Database, cloned.Options,
	)
}

// migrationRole matches frappe_migration in deployments/database/
// initialize.sql: the schema-owning role, granted CREATEROLE the same
// way, so a test using NewOwner can create tables, extensions and
// policies exactly as cmd/migrate does.
func migrationRole() pgtestdb.Role {
	return pgtestdb.Role{
		Username:     migrationRoleUsername,
		Password:     migrationRolePassword,
		Capabilities: migrationRoleCapability,
	}
}
