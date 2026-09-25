package database

import (
	"context"
	"fmt"

	"github.com/exaring/otelpgx"
	"github.com/jackc/pgx/v5/pgxpool"
)

// DependencyName identifies the database dependency for logging and error
// reporting when the composition root registers it as an
// application.Dependency[*pgxpool.Pool].
const DependencyName = "database"

// Up builds a connection pool from settings, ready to be wired as an
// application.Dependency[*pgxpool.Pool]'s Up function. Every connection is
// instrumented with otelpgx: queries are traced (without logging SQL
// argument values, so no query parameter ever reaches a span or a log
// record) and pool statistics are exported as metrics. Up pings the pool
// once before returning, so a misconfigured or unreachable database fails
// fast at startup instead of on the first query.
func Up(ctx context.Context, settings Settings) (*pgxpool.Pool, error) {
	poolConfig, err := poolConfiguration(settings)
	if err != nil {
		return nil, err
	}

	pool, err := pgxpool.NewWithConfig(ctx, poolConfig)
	if err != nil {
		return nil, fmt.Errorf("create database pool: %w", err)
	}

	if err := otelpgx.RecordStats(pool); err != nil {
		pool.Close()
		return nil, fmt.Errorf("record database pool stats: %w", err)
	}

	if err := pool.Ping(ctx); err != nil {
		pool.Close()
		return nil, fmt.Errorf("ping database: %w", err)
	}

	return pool, nil
}

// poolConfiguration validates the raw settings, then applies defaults to
// zero-valued fields, and translates the result into a pgxpool.Config.
// Validating before defaulting keeps an explicit, meaningful zero (such as
// MinConnections) from being silently replaced.
func poolConfiguration(settings Settings) (*pgxpool.Config, error) {
	if err := settings.Validate(); err != nil {
		return nil, err
	}
	settings = settings.withDefaults()

	poolConfig, err := pgxpool.ParseConfig(settings.URL)
	if err != nil {
		return nil, fmt.Errorf("parse database url: %w", err)
	}

	poolConfig.MaxConns = settings.MaxConnections
	poolConfig.MinConns = settings.MinConnections
	poolConfig.MaxConnLifetime = settings.MaxConnectionLifetime
	poolConfig.MaxConnIdleTime = settings.MaxConnectionIdleTime
	poolConfig.ConnConfig.ConnectTimeout = settings.ConnectTimeout
	poolConfig.ConnConfig.Tracer = otelpgx.NewTracer(
		otelpgx.WithDisableSQLStatementInAttributes(),
	)
	return poolConfig, nil
}

// Down closes pool, ready to be wired as an application.Dependency[*pgxpool.
// Pool]'s Down function. It is a no-op when pool is nil.
func Down(_ context.Context, pool *pgxpool.Pool) error {
	if pool != nil {
		pool.Close()
	}
	return nil
}

// Check pings pool, ready to be wired as an application.Dependency[*pgxpool.
// Pool]'s Check function.
func Check(ctx context.Context, pool *pgxpool.Pool) error {
	return pool.Ping(ctx)
}
