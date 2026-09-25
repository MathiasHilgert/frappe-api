package valkey

import (
	"errors"
	"fmt"
	"net"
	"time"
)

// Default values applied by Up, after Validate, to zero-valued fields.
const (
	DefaultDialTimeout  = 5 * time.Second
	DefaultWriteTimeout = 5 * time.Second
)

// Settings configures the client built by Up.
type Settings struct {
	// Address is the server's host:port. Required.
	Address string
	// Password authenticates the connection. Optional; treat it as a
	// secret.
	Password string
	// Database is the logical database number selected on connect.
	Database int
	// DialTimeout bounds establishing one connection. Defaults to
	// DefaultDialTimeout when zero.
	DialTimeout time.Duration
	// WriteTimeout bounds writing a command to a connection, which also
	// detects dead connections. Defaults to DefaultWriteTimeout when zero.
	WriteTimeout time.Duration
}

// Validate checks the raw settings, before defaults are applied.
func (settings Settings) Validate() error {
	if settings.Address == "" {
		return errors.New("valkey: address must not be empty")
	}
	if _, _, err := net.SplitHostPort(settings.Address); err != nil {
		return fmt.Errorf("valkey: address must be host:port: %w", err)
	}
	if settings.Database < 0 {
		return fmt.Errorf("valkey: database must not be negative, got %d", settings.Database)
	}
	if settings.DialTimeout < 0 || settings.WriteTimeout < 0 {
		return errors.New("valkey: timeouts must not be negative")
	}
	return nil
}

func (settings Settings) withDefaults() Settings {
	if settings.DialTimeout == 0 {
		settings.DialTimeout = DefaultDialTimeout
	}
	if settings.WriteTimeout == 0 {
		settings.WriteTimeout = DefaultWriteTimeout
	}
	return settings
}
