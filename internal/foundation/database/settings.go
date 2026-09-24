package database

import (
	"fmt"
	"time"
)

// Default values applied by Up when Settings leaves a field at its zero
// value.
const (
	DefaultMaxConnections        = int32(10)
	DefaultMinConnections        = int32(2)
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
	// Defaults to DefaultMaxConnections.
	MaxConnections int32
	// MinConnections is how many connections the pool keeps open, ready,
	// even when idle. Defaults to DefaultMinConnections.
	MinConnections int32
	// MaxConnectionLifetime bounds how long a connection may be reused
	// before it is closed and replaced. Defaults to
	// DefaultMaxConnectionLifetime.
	MaxConnectionLifetime time.Duration
	// MaxConnectionIdleTime bounds how long a connection may sit idle in
	// the pool before it is closed. Defaults to
	// DefaultMaxConnectionIdleTime.
	MaxConnectionIdleTime time.Duration
	// ConnectTimeout bounds how long establishing one connection may
	// take. Defaults to DefaultConnectTimeout.
	ConnectTimeout time.Duration
}

// withDefaults returns settings with every zero-valued field replaced by
// its package default.
func (settings Settings) withDefaults() Settings {
	if settings.MaxConnections <= 0 {
		settings.MaxConnections = DefaultMaxConnections
	}
	if settings.MinConnections <= 0 {
		settings.MinConnections = DefaultMinConnections
	}
	if settings.MaxConnectionLifetime <= 0 {
		settings.MaxConnectionLifetime = DefaultMaxConnectionLifetime
	}
	if settings.MaxConnectionIdleTime <= 0 {
		settings.MaxConnectionIdleTime = DefaultMaxConnectionIdleTime
	}
	if settings.ConnectTimeout <= 0 {
		settings.ConnectTimeout = DefaultConnectTimeout
	}
	return settings
}

// Validate checks that settings's explicitly set fields are consistent,
// before defaults are applied by withDefaults. It rejects a blank URL, any
// negative duration or connection count, and a MinConnections greater than
// MaxConnections when both are explicitly set.
func (settings Settings) Validate() error {
	if settings.URL == "" {
		return fmt.Errorf("database: url must not be empty")
	}
	if settings.MaxConnections < 0 {
		return fmt.Errorf("database: max connections must not be negative, got %d", settings.MaxConnections)
	}
	if settings.MinConnections < 0 {
		return fmt.Errorf("database: min connections must not be negative, got %d", settings.MinConnections)
	}
	if settings.MaxConnections > 0 && settings.MinConnections > settings.MaxConnections {
		return fmt.Errorf("database: min connections (%d) must not exceed max connections (%d)", settings.MinConnections, settings.MaxConnections)
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
