//go:build integration

package databasetest

import (
	"context"
	"crypto/sha256"
	"database/sql"
	"encoding/hex"
	"fmt"
	"io/fs"

	"github.com/peterldowns/pgtestdb"
	"github.com/pressly/goose/v3"

	"github.com/MathiasHilgert/frappe-api/migrations"
)

// templateMigrator is the pgtestdb.Migrator for the template database. It
// replaces pgtestdb's goosemigrator, which only applies SQL files: it also
// registers migrations.GoMigrations (the data seeds) exactly as
// cmd/migrate does (see cmd/migrate/go_migrations.go), and its hash covers
// the embedded data files, so a new snapshot rebuilds the template.
type templateMigrator struct{}

var _ pgtestdb.Migrator = templateMigrator{}

// Hash digests every file of migrations.FS and migrations.Data, path and
// content, plus every Go migration version and revision.
func (templateMigrator) Hash() (string, error) {
	hash := sha256.New()
	for _, fileSystem := range []fs.FS{migrations.FS, migrations.Data} {
		err := fs.WalkDir(fileSystem, ".", func(path string, entry fs.DirEntry, walkError error) error {
			if walkError != nil || entry.IsDir() {
				return walkError
			}
			content, err := fs.ReadFile(fileSystem, path)
			if err != nil {
				return err
			}
			_, _ = fmt.Fprintf(hash, "%s\x00%d\x00", path, len(content))
			_, _ = hash.Write(content)
			return nil
		})
		if err != nil {
			return "", fmt.Errorf("databasetest: hash migrations: %w", err)
		}
	}
	for _, migration := range migrations.GoMigrations() {
		_, _ = fmt.Fprintf(hash, "go\x00%d\x00%s\x00", migration.Version, migration.Revision)
	}
	return hex.EncodeToString(hash.Sum(nil)), nil
}

// Migrate applies every SQL and Go migration to the template database.
func (templateMigrator) Migrate(ctx context.Context, database *sql.DB, _ pgtestdb.Config) error {
	registered := make([]*goose.Migration, 0, len(migrations.GoMigrations()))
	for _, migration := range migrations.GoMigrations() {
		registered = append(registered, goose.NewGoMigration(
			migration.Version,
			&goose.GoFunc{RunDB: migration.Up},
			&goose.GoFunc{RunDB: migration.Down},
		))
	}
	provider, err := goose.NewProvider(goose.DialectPostgres, database, migrations.FS,
		goose.WithAllowOutofOrder(true),
		goose.WithGoMigrations(registered...),
	)
	if err != nil {
		return fmt.Errorf("databasetest: create migration provider: %w", err)
	}
	if _, err := provider.Up(ctx); err != nil {
		return fmt.Errorf("databasetest: apply migrations: %w", err)
	}
	return nil
}
