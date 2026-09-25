// Package migrations embeds every SQL migration file as migrations.FS.
// This package is intentionally thin: it must never import a migration
// tool. Only cmd/migrate is allowed to import the migration tool package
// (currently github.com/pressly/goose/v3, still under discussion and kept
// swappable) and turn migrations.FS into applied schema changes; no
// module and no other foundation package may import migrations at all
// (see .go-arch-lint.yml).
//
// # File format
//
// Each file is a goose SQL migration:
//
//	-- +goose Up
//	-- +goose StatementBegin
//	ALTER DEFAULT PRIVILEGES ...;
//	-- +goose StatementEnd
//
//	-- +goose Down
//	-- +goose StatementBegin
//	ALTER DEFAULT PRIVILEGES ...;
//	-- +goose StatementEnd
//
// The StatementBegin/StatementEnd markers are required whenever a
// statement itself contains a semicolon that must not be treated as a
// statement separator (for example a function body); goose splits on
// semicolons outside of such blocks.
//
// # Go migrations
//
// Data seeds a SQL file cannot express (streaming a compressed snapshot
// through COPY) are plain Go functions listed by GoMigrations, reading
// their files from Data. They carry a version like any SQL file; this
// package still never imports the migration tool: cmd/migrate and the
// databasetest template register them with goose.
//
// # Versioning
//
// Files are named "<timestamp>_<description>.sql" (goose's default
// timestamp versioning, not sequential numbers): "task migrations:create"
// generates that timestamp prefix. Timestamp versioning lets migrations
// authored on parallel feature branches coexist without a numbering
// collision; cmd/migrate enables goose's allow-out-of-order-migrations
// option so one branch's later timestamp being applied before another
// branch's earlier, not-yet-merged timestamp is not treated as an error.
package migrations
