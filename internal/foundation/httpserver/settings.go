package httpserver

import (
	"log/slog"
	"time"
)

// Readiness reports whether the application is ready to serve traffic,
// backing the /health/ready probe. It is a small, consumer-side interface
// (declared here, not by whatever implements it) satisfied today by
// internal/foundation/application.Application itself, which already has a
// Ready() bool method, so this package never imports the application
// package directly (foundation packages must not import each other). It
// is intentionally this narrow so a future, HTTP-independent dependency
// health-checking foundation package can implement it and be swapped in
// without this package changing: httpserver never pings a dependency
// itself, it only asks whatever Readiness it was given.
type Readiness interface {
	Ready() bool
	// Report returns the readiness detail served as the /health/ready
	// response body. Its concrete type is owned by whatever implements
	// Readiness (for example internal/foundation/health.Report), and this
	// package only ever marshals it as JSON; it never inspects its
	// fields, keeping httpserver decoupled from the health package.
	Report() any
}

// alwaysReady is the Readiness used when Settings.Ready is nil.
type alwaysReady struct{}

func (alwaysReady) Ready() bool { return true }
func (alwaysReady) Report() any { return struct{}{} }

// Settings configures a Server. Every field has a corresponding field on
// internal/foundation/configuration.Configuration's HTTP struct; the
// composition root is responsible for translating one into the other.
type Settings struct {
	// Logger receives access log and recovered-panic log records. If nil,
	// slog.Default() is used.
	Logger *slog.Logger
	// Ready reports application readiness for the /health/ready probe.
	// If nil, the server is treated as always ready. /health/live never
	// consults it and stays dependency-free.
	Ready Readiness
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
	// DrainDelay is how long Shutdown waits, still serving traffic and
	// reporting not-ready, before it starts the actual graceful
	// shutdown. It gives a load balancer or Kubernetes time to notice
	// /health/ready has turned unhealthy and stop routing new requests
	// here, before connections start being closed. It respects context
	// cancellation, so a caller in a hurry can still cut it short.
	DrainDelay time.Duration
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

// ready returns the configured Readiness, falling back to an
// always-ready implementation when none was set.
func (settings Settings) ready() Readiness {
	if settings.Ready != nil {
		return settings.Ready
	}
	return alwaysReady{}
}
