// Package postgres implements the geo application ports on the seeded
// reference tables (places, countries, subdivisions, cities, time_zones,
// place_names, geo_data_versions), one repository per resource. It only
// reads: the application role has SELECT and nothing else on them.
//
// Localized names come from place_names in the request's locale and fall
// back to places.name. Every statement starts with a "-- name:" line, so
// its database span is named after it (see internal/foundation/database).
package postgres

import (
	"context"
	"errors"
	"fmt"

	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgxpool"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
)

// ErrPoolUnavailable reports a read before the database pool is up.
var ErrPoolUnavailable = errors.New("geo: database pool is not ready")

// PoolSource hands out the application pool once it is up;
// *application.Handle[*pgxpool.Pool] implements it.
type PoolSource interface {
	Get() (*pgxpool.Pool, bool)
}

// connection runs the repositories' statements on the pool once it is up.
type connection struct {
	source PoolSource
}

// query runs statement with arguments.
func (connection connection) query(ctx context.Context, statement string, arguments ...any) (pgx.Rows, error) {
	pool, ready := connection.source.Get()
	if !ready || pool == nil {
		return nil, ErrPoolUnavailable
	}
	rows, err := pool.Query(ctx, statement, arguments...)
	if err != nil {
		return nil, fmt.Errorf("geo query: %w", err)
	}
	return rows, nil
}

// locale is the place_names locale matching locale, or "" (no localized
// name, so every name falls back to places.name) for none.
func (connection) locale(locale i18n.Locale) string {
	if locale.IsZero() {
		return ""
	}
	return locale.String()
}

// optional is value as a nullable statement argument: nil (no filter)
// when empty.
func (connection) optional(value string) *string {
	if value == "" {
		return nil
	}
	return &value
}
