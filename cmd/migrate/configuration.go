package main

import (
	"fmt"

	"github.com/caarlos0/env/v11"
)

// configuration is cmd/migrate's own, small configuration struct. It
// deliberately does not reuse internal/foundation/configuration.
// Configuration: that struct describes the running API and would grow an
// unrelated field (the migration role's connection string) that the API
// itself must never read, since the API only ever connects as the
// application role.
type configuration struct {
	// MigrationURL is the Postgres connection string for the migration
	// (schema-owning) role. Required; treat it as a secret.
	MigrationURL string `env:"DATABASE_MIGRATION_URL" validate:"required"`
}

// loadConfiguration reads configuration from environment variables and
// validates that MigrationURL was set.
func loadConfiguration() (configuration, error) {
	var loaded configuration

	if err := env.Parse(&loaded); err != nil {
		return configuration{}, fmt.Errorf("parse migrate configuration from environment: %w", err)
	}
	if loaded.MigrationURL == "" {
		return configuration{}, fmt.Errorf("DATABASE_MIGRATION_URL must be set")
	}

	return loaded, nil
}
