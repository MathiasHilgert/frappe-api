package main

import (
	"github.com/pressly/goose/v3"

	"github.com/MathiasHilgert/frappe-api/migrations"
)

// goMigrations registers migrations.GoMigrations with goose. The
// migrations package stays free of the migration tool; this adapter is
// the only place that turns its plain Go migrations into goose ones.
// Each runs outside goose's transaction (RunDB), since it manages its own
// pgx transaction to stream data through COPY.
func goMigrations() goose.ProviderOption {
	registered := make([]*goose.Migration, 0, len(migrations.GoMigrations()))
	for _, migration := range migrations.GoMigrations() {
		registered = append(registered, goose.NewGoMigration(
			migration.Version,
			&goose.GoFunc{RunDB: migration.Up},
			&goose.GoFunc{RunDB: migration.Down},
		))
	}
	return goose.WithGoMigrations(registered...)
}
