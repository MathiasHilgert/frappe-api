package httpserver

import (
	"log/slog"
	"time"
)

// ReadyFunc reports whether the application is ready to serve traffic. It
// is injected by the composition root, typically as an
// internal/foundation/application.Application's Ready method, so this
// package never imports the application package directly (foundation
// packages must not import each other).
type ReadyFunc func() bool

// Settings configures a Server. Every field has a corresponding field on
// internal/foundation/configuration.Configuration's HTTP struct; the
// composition root is responsible for translating one into the other.
type Settings struct {
	// Logger receives access log and recovered-panic log records. If nil,
	// slog.Default() is used.
	Logger *slog.Logger
	// Ready reports application readiness for the /health/ready probe.
	// If nil, the server is treated as always ready.
	Ready ReadyFunc
	// Title is the OpenAPI document's title.
	Title string
	// Version is the OpenAPI document's version, typically the running
	// build's version.
	Version string
	// MaxBodyBytes bounds the size of a request body, in bytes. A request
	// whose body exceeds this limit is rejected.
	MaxBodyBytes int64
	// ReadHeaderTimeout bounds how long reading request headers may take.
	ReadHeaderTimeout time.Duration
	// ReadTimeout bounds how long reading the full request may take.
	ReadTimeout time.Duration
	// WriteTimeout bounds how long writing the response may take.
	WriteTimeout time.Duration
	// IdleTimeout bounds how long a keep-alive connection may sit idle.
	IdleTimeout time.Duration
	// Port is the TCP port Listen binds to. Zero lets the operating
	// system choose a free port, which is useful in tests.
	Port int
	// MaxHeaderBytes bounds the size of request headers, in bytes.
	MaxHeaderBytes int
	// DocumentationEnabled toggles the /docs UI and /openapi.json spec.
	DocumentationEnabled bool
}

// logger returns the configured logger, falling back to slog.Default()
// when none was set.
func (settings Settings) logger() *slog.Logger {
	if settings.Logger != nil {
		return settings.Logger
	}
	return slog.Default()
}

// ready returns the configured ready function, falling back to an
// always-ready function when none was set.
func (settings Settings) ready() ReadyFunc {
	if settings.Ready != nil {
		return settings.Ready
	}
	return func() bool { return true }
}
