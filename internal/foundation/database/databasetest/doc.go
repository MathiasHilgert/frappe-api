//go:build integration

// Package databasetest gives integration tests an isolated Postgres
// database per test, backed by one shared container per "go test"
// invocation and one migrated template database, so no test ever
// migrates a fresh database from scratch and no test starts its own
// container.
//
// # Architecture
//
//   - One Postgres container (testcontainers-go, pinned to the same
//     postgres:18.1 image as compose.yaml) is started lazily, on first
//     use, and shared by every package test binary of the same "go test"
//     invocation through testcontainers.WithReuseByName. The name embeds
//     a hash of testcontainers.SessionID, which is derived from the
//     parent "go test" process, so a concurrent second invocation gets
//     its own container instead of attaching to one that Ryuk removes
//     when the first invocation ends. Nothing is reused across
//     invocations: Ryuk reaps the container after the run. It is tuned
//     for throughput, not durability (fsync, synchronous_commit and
//     full_page_writes disabled; /var/lib/postgresql, which contains
//     postgres:18's default PGDATA /var/lib/postgresql/18/docker, on
//     tmpfs).
//   - Each pool this package returns is capped at 4 connections (and
//     asks for 0 idle ones) against the container's max_connections=500;
//     see testPoolMaxConnections in pool.go for the budget.
//   - The roles documented in internal/foundation/database/doc.go
//     (frappe_migration, frappe_application) and the outbox relay role
//     (frappe_outbox_relay) are created once against that container, under
//     a transaction-scoped advisory lock so concurrent package binaries
//     cannot race, from the same statements as
//     deployments/database/initialize.sql, so
//     migrations that grant privileges "TO frappe_application" by name
//     apply exactly as they do against a real deployment.
//   - github.com/peterldowns/pgtestdb migrates one template database, as
//     frappe_migration, keyed by a hash of migrations.FS and migrations.Data (see
//     templateMigrator, which also applies migrations.GoMigrations), then clones that
//     template into a fresh, isolated database for every call to New or
//     NewOwner. A test is never migrated directly: cloning a
//     already-migrated template is what makes many parallel tests cheap.
//
// # Usage
//
//	func TestIntegrationSomething(t *testing.T) {
//		t.Parallel()
//
//		pool := databasetest.New(t) // connected as frappe_application
//		defer pool.Close()
//
//		// ... use pool exactly like the application does, including
//		// database.WithinTransaction for Row Level Security-scoped work.
//	}
//
// A test that needs to create a table, extension or Row Level Security
// policy that only the schema owner can create (for example a scratch
// table with FORCE ROW LEVEL SECURITY used to probe a policy), and does
// not also need to query it back as frappe_application, should use
// NewOwner instead, which connects as frappe_migration, the same role
// cmd/migrate uses. A test that needs both roles against the exact same
// database, such as one proving a Row Level Security policy actually
// restricts frappe_application, should use NewWithOwner instead of
// mixing New and NewOwner (each provisions its own, separate database).
//
// # Restricted to integration tests
//
// This package must be imported only from a "_test.go" file guarded by
// "//go:build integration" (see .golangci.yml's depguard rules and
// .go-arch-lint.yml's foundation_database_test component, which is the
// only component allowed to depend on it). It pulls in testcontainers-go
// and pgtestdb, both test-only dependencies that a production binary must
// never link.
package databasetest
