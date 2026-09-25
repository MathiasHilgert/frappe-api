package database

import (
	"fmt"
	"time"
)

// Default values applied by Up, after Validate, when Settings leaves a
// field at its zero value. MinConnections has no default: zero is a valid
// minimum (the pool then opens connections only on demand).
const (
	DefaultMaxConnections        = int32(10)
	DefaultMaxConnectionLifetime = 30 * time.Minute
	DefaultMaxConnectionIdleTime = 5 * time.Minute
	DefaultConnectTimeout        = 5 * time.Second
)

// Settings configures the connection pool built by Up.
type Settings struct {
	// URL is the Postgres connection string for the application role.
	// Required; treat it as a secret.
	URL string
	// MaxConnections bounds how many connections the pool may open.
	// Defaults to DefaultMaxConnections when zero.
	MaxConnections int32
	// MinConnections is how many connections the pool keeps open, ready,
	// even when idle. Zero is valid and kept as is (connections are then
	// opened only on demand). It must not exceed MaxConnections, or
	// DefaultMaxConnections when MaxConnections is zero.
	MinConnections int32
	// MaxConnectionLifetime bounds how long a connection may be reused
	// before it is closed and replaced. Defaults to
	// DefaultMaxConnectionLifetime when zero.
	MaxConnectionLifetime time.Duration
	// MaxConnectionIdleTime bounds how long a connection may sit idle in
	// the pool before it is closed. Defaults to
	// DefaultMaxConnectionIdleTime when zero.
	MaxConnectionIdleTime time.Duration
	// ConnectTimeout bounds how long establishing one connection may
	// take. Defaults to DefaultConnectTimeout when zero.
	ConnectTimeout time.Duration
}

// withDefaults returns settings with every zero-valued field that has a
// package default replaced by it. MinConnections is deliberately left
// untouched, since zero is a meaningful value for it. Call it only after
// Validate has accepted the raw settings.
func (settings Settings) withDefaults() Settings {
	if settings.MaxConnections == 0 {
		settings.MaxConnections = DefaultMaxConnections
	}
	if settings.MaxConnectionLifetime == 0 {
		settings.MaxConnectionLifetime = DefaultMaxConnectionLifetime
	}
	if settings.MaxConnectionIdleTime == 0 {
		settings.MaxConnectionIdleTime = DefaultMaxConnectionIdleTime
	}
	if settings.ConnectTimeout == 0 {
		settings.ConnectTimeout = DefaultConnectTimeout
	}
	return settings
}

// Validate checks the raw settings, before withDefaults fills zero-valued
// fields. It rejects a blank URL, any negative duration or connection
// count, and a MinConnections greater than the effective MaxConnections
// (MaxConnections, or DefaultMaxConnections when MaxConnections is zero).
func (settings Settings) Validate() error {
	if settings.URL == "" {
		return fmt.Errorf("database: url must not be empty")
	}
	if err := settings.validateConnectionCounts(); err != nil {
		return err
	}
	if settings.MaxConnectionLifetime < 0 {
		return fmt.Errorf("database: max connection lifetime must not be negative, got %s", settings.MaxConnectionLifetime)
	}
	if settings.MaxConnectionIdleTime < 0 {
		return fmt.Errorf("database: max connection idle time must not be negative, got %s", settings.MaxConnectionIdleTime)
	}
	if settings.ConnectTimeout < 0 {
		return fmt.Errorf("database: connect timeout must not be negative, got %s", settings.ConnectTimeout)
	}
	return nil
}

// validateConnectionCounts rejects negative connection counts and a
// MinConnections above the effective MaxConnections.
func (settings Settings) validateConnectionCounts() error {
	if settings.MaxConnections < 0 {
		return fmt.Errorf("database: max connections must not be negative, got %d", settings.MaxConnections)
	}
	if settings.MinConnections < 0 {
		return fmt.Errorf("database: min connections must not be negative, got %d", settings.MinConnections)
	}
	effectiveMaxConnections := settings.MaxConnections
	if effectiveMaxConnections == 0 {
		effectiveMaxConnections = DefaultMaxConnections
	}
	if settings.MinConnections > effectiveMaxConnections {
		return fmt.Errorf("database: min connections (%d) must not exceed max connections (%d)", settings.MinConnections, effectiveMaxConnections)
	}
	return nil
}
