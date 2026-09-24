// Package database is the foundation layer for Postgres access: pool
// construction, observability and a Row Level Security (RLS) helper. It
// wires github.com/jackc/pgx/v5/pgxpool into the application lifecycle as
// an application.Dependency and exposes WithinTransaction for RLS-scoped
// work. This package stays generic: it knows no business tables and no
// tenant model.
//
// # Two-role model
//
// Two distinct Postgres roles are used, on purpose:
//
//   - A migration role (frappe_migration in local development, see
//     deployments/database/initialize.sql) owns the schema and every
//     object created in it. It is used only by cmd/migrate, never by the
//     running API, and its connection string is DATABASE_MIGRATION_URL.
//   - An application role (frappe_application) is granted only the
//     privileges it needs at runtime (SELECT, INSERT, UPDATE, DELETE on
//     tables, USAGE on sequences; see migrations/00001_application_role_
//     privileges.sql) and is NOSUPERUSER, NOBYPASSRLS. It is the role
//     this package's pool connects as, through DATABASE_URL.
//
// Separating the two roles means a bug or a compromised connection in the
// running API can never alter schema, and Row Level Security cannot be
// silently bypassed by a role with BYPASSRLS. Business tables must be
// created with FORCE ROW LEVEL SECURITY, so even the table owner's own
// queries are subject to the policies below (FORCE is required because,
// by default, Postgres exempts the table owner from Row Level Security).
//
// # Row Level Security helper
//
// WithinTransaction begins a transaction, applies every setting in
// TransactionSettings with SELECT set_config($1, $2, true), runs the given
// work, and commits or rolls back. Settings are applied with the
// is_local argument to set_config hard-coded to true, making every
// setting transaction-local (it reverts at COMMIT or ROLLBACK), and NEVER
// with a session-level SET. This is deliberate: pgxpool hands out pooled
// connections to different requests over time, so a session-level SET
// would leak one request's or one tenant's setting into whichever request
// reuses that connection next. Business modules read these settings back
// from Row Level Security policies with current_setting('application.
// tenant', true), the second argument making it return an empty string
// instead of erroring when unset.
//
// # Unit-of-work convention for modules
//
// Application layers (commands/queries) must never import this package,
// pgx or any concrete driver (see .golangci.yml's depguard rules and
// .go-arch-lint.yml, which keep foundation out of module_application's
// allowed dependencies). Instead, a module's application layer declares
// its own small, neutral port for atomicity, named to fit the use case,
// for example:
//
//	package application
//
//	type Atomic interface {
//		Run(ctx context.Context, work func(ctx context.Context) error) error
//	}
//
// The module's Postgres adapter (internal/modules/<module>/adapters/...)
// implements that port on top of WithinTransaction:
//
//	package postgres
//
//	type atomic struct{ pool *pgxpool.Pool }
//
//	func (a atomic) Run(ctx context.Context, work func(ctx context.Context) error) error {
//		return database.WithinTransaction(ctx, a.pool, database.TransactionSettings{
//			"application.tenant": tenant.FromContext(ctx),
//		}, func(ctx context.Context, _ pgx.Tx) error {
//			return work(ctx)
//		})
//	}
//
// A repository method in the same adapter package recovers the
// transaction, if one is open, with TransactionFromContext and runs its
// query on it so it joins the ongoing unit of work (and inherits the Row
// Level Security settings applied to it); with no open transaction, it
// falls back to querying the pool directly:
//
//	func (r repository) Save(ctx context.Context, record Record) error {
//		querier := r.pool
//		if transaction, ok := database.TransactionFromContext(ctx); ok {
//			querier = transaction
//		}
//		_, err := querier.Exec(ctx, "INSERT INTO ...", ...)
//		return err
//	}
package database
