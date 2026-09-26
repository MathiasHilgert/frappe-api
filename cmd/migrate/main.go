// Command migrate applies, rolls back and reports the status of database
// schema migrations. It is a separate binary from cmd/api on purpose:
// running migrations at API startup is unsafe with more than one replica,
// since concurrent replicas would race to apply the same migration. An
// operator (or a deploy pipeline step) runs this binary once, before or
// alongside a rolling deploy of cmd/api, instead.
//
// Usage:
//
//	migrate up|down|status|version
//
// migrate connects with the migration (schema-owning) role, never the
// application role cmd/api uses; see internal/foundation/database/doc.go
// for why the two are kept separate.
package main

import (
	"context"
	"database/sql"
	"fmt"
	"os"
	"os/signal"
	"syscall"

	"github.com/pressly/goose/v3"
	"github.com/pressly/goose/v3/lock"

	// Registers the "pgx" database/sql driver name used by sql.Open below.
	_ "github.com/jackc/pgx/v5/stdlib"

	"github.com/MathiasHilgert/frappe-api/migrations"
)

func main() {
	ctx, stop := signalContext(context.Background())
	err := run(ctx, os.Args[1:])
	stop()
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}

// signalContext returns a copy of parent that is canceled on SIGINT or
// SIGTERM, so an operator's Ctrl+C or an orchestrator's stop request
// cancels the in-flight migration through ctx, letting deferred cleanup
// (closing the database handle) run, instead of the default signal
// behavior of killing the process outright. Call stop to release the signal
// handler.
func signalContext(parent context.Context) (ctx context.Context, stop context.CancelFunc) {
	return signal.NotifyContext(parent, os.Interrupt, syscall.SIGTERM)
}

func run(ctx context.Context, args []string) error {
	if len(args) < 1 {
		return fmt.Errorf("usage: migrate up|down|status|version")
	}
	command := args[0]

	loadedConfiguration, err := loadConfiguration()
	if err != nil {
		return err
	}

	database, err := sql.Open("pgx", loadedConfiguration.MigrationURL)
	if err != nil {
		return fmt.Errorf("open database: %w", err)
	}
	defer func() { _ = database.Close() }()

	// A session-level Postgres advisory lock (see
	// lock.NewPostgresSessionLocker) serializes concurrent "migrate up"
	// invocations, so two deploy pipelines or two operators running this
	// binary at the same time cannot apply the same migration twice.
	sessionLocker, err := lock.NewPostgresSessionLocker()
	if err != nil {
		return fmt.Errorf("create migration session locker: %w", err)
	}

	provider, err := goose.NewProvider(
		goose.DialectPostgres,
		database,
		migrations.FS,
		goose.WithSessionLocker(sessionLocker),
		// Migration files are versioned by timestamp (see migrations/
		// doc.go), not sequential numbers, so two feature branches can
		// each add a migration without colliding on a shared next
		// number. WithAllowOutofOrder lets a later-timestamped migration
		// that was merged first be applied before an earlier-timestamped
		// one that has not merged yet, instead of goose refusing to
		// proceed.
		goose.WithAllowOutofOrder(true),
		// Go migrations (data seeds streamed through COPY) run in version
		// order with the SQL files; see go_migrations.go.
		goMigrations(),
	)
	if err != nil {
		return fmt.Errorf("create migration provider: %w", err)
	}

	switch command {
	case "up":
		return runUp(ctx, provider)
	case "down":
		return runDown(ctx, provider)
	case "status":
		return runStatus(ctx, provider)
	case "version":
		return runVersion(ctx, provider)
	default:
		return fmt.Errorf("unknown command %q: usage: migrate up|down|status|version", command)
	}
}

func runUp(ctx context.Context, provider *goose.Provider) error {
	results, err := provider.Up(ctx)
	if err != nil {
		return fmt.Errorf("apply migrations: %w", err)
	}
	for _, result := range results {
		fmt.Printf("applied %s (%s)\n", result.Source.Path, result.Duration)
	}
	return nil
}

func runDown(ctx context.Context, provider *goose.Provider) error {
	result, err := provider.Down(ctx)
	if err != nil {
		return fmt.Errorf("roll back migration: %w", err)
	}
	fmt.Printf("rolled back %s (%s)\n", result.Source.Path, result.Duration)
	return nil
}

func runStatus(ctx context.Context, provider *goose.Provider) error {
	statuses, err := provider.Status(ctx)
	if err != nil {
		return fmt.Errorf("get migration status: %w", err)
	}
	for _, status := range statuses {
		fmt.Printf("%s\t%s\n", status.Source.Path, status.State)
	}
	return nil
}

func runVersion(ctx context.Context, provider *goose.Provider) error {
	version, err := provider.GetDBVersion(ctx)
	if err != nil {
		return fmt.Errorf("get migration version: %w", err)
	}
	fmt.Println(version)
	return nil
}
