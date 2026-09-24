// Package configuration declares the application-wide Configuration
// structure and the Provider port used to load it. Concrete providers
// (for example an environment-variable provider) live in subpackages and
// implement Provider.
//
// # Telemetry environment variables
//
// TELEMETRY_ENABLED (default true) toggles the telemetry SDK in
// internal/foundation/telemetry: when true, it exports traces, metrics
// and logs through the OpenTelemetry SDK; when false, telemetry stays
// wired to the no-op global OpenTelemetry providers.
//
// Because TELEMETRY_ENABLED defaults to true, running the program locally
// without a collector listening at OTEL_EXPORTER_OTLP_ENDPOINT (default
// http://localhost:4318) makes every export attempt fail. Either start
// the bundled local collector stack first, with `task observability:up`
// (see Taskfile.yml and compose.yaml), or set TELEMETRY_ENABLED=false to
// run without exporting at all.
//
// The OTLP exporter endpoint and headers are not declared as fields on
// this Configuration: they are read directly by the OpenTelemetry SDK
// from the standard OTEL_EXPORTER_OTLP_* environment variables, most
// commonly:
//
//   - OTEL_EXPORTER_OTLP_ENDPOINT: base endpoint for traces, metrics and
//     logs (for example http://localhost:4318 for OTLP over HTTP).
//   - OTEL_EXPORTER_OTLP_TRACES_ENDPOINT,
//     OTEL_EXPORTER_OTLP_METRICS_ENDPOINT,
//     OTEL_EXPORTER_OTLP_LOGS_ENDPOINT: per-signal endpoint overrides.
//   - OTEL_EXPORTER_OTLP_HEADERS: comma-separated key=value headers sent
//     with every export request (for example an authentication token).
//   - OTEL_EXPORTER_OTLP_PROTOCOL: export protocol; this application's
//     exporters are configured for "http/protobuf".
//
// See the OpenTelemetry specification for the full list.
package configuration

import (
	"context"
	"time"
)

// Configuration is the full, validated configuration for the running
// program. It is composed of one struct per concern, each with its own
// environment prefix.
type Configuration struct {
	Logging     Logging     `envPrefix:"LOGGING_"`
	Application Application `envPrefix:"APPLICATION_"`
	Database    Database    `envPrefix:"DATABASE_"`
	HTTP        HTTP        `envPrefix:"HTTP_"`
	Health      Health      `envPrefix:"HEALTH_"`
	Telemetry   Telemetry   `envPrefix:"TELEMETRY_"`
}

// Application holds identity and deployment environment settings.
type Application struct {
	// Name identifies the running program in logs and telemetry.
	Name string `env:"NAME" envDefault:"frappe-api" validate:"required"`
	// Environment is the deployment environment the program runs in.
	Environment string `env:"ENVIRONMENT" validate:"required,oneof=development staging production"`
	// HookTimeout bounds how long a single lifecycle hook's Up or Down
	// call may take before it is canceled. It is unrelated to any HTTP
	// server shutdown timeout, which a future HTTP module will declare
	// under its own HTTP_ prefix.
	HookTimeout time.Duration `env:"HOOK_TIMEOUT" envDefault:"30s" validate:"required,gt=0"`
}

// Telemetry holds settings for the telemetry SDK (tracing, metrics and
// logging export).
type Telemetry struct {
	// Enabled controls whether the telemetry SDK exports data through a
	// real OTLP pipeline. When false, telemetry stays wired to no-op
	// global providers.
	Enabled bool `env:"ENABLED" envDefault:"true"`
}

// HTTP holds settings for the HTTP server.
type HTTP struct {
	// Port is the TCP port the HTTP server listens on.
	Port int `env:"PORT" envDefault:"8080" validate:"min=1,max=65535"`
	// ShutdownTimeout bounds how long graceful shutdown may take.
	ShutdownTimeout time.Duration `env:"SHUTDOWN_TIMEOUT" envDefault:"10s" validate:"required,gt=0"`
	// ReadHeaderTimeout bounds how long reading request headers may take.
	ReadHeaderTimeout time.Duration `env:"READ_HEADER_TIMEOUT" envDefault:"5s" validate:"required,gt=0"`
	// ReadTimeout bounds how long reading the full request, including the
	// body, may take.
	ReadTimeout time.Duration `env:"READ_TIMEOUT" envDefault:"10s" validate:"required,gt=0"`
	// WriteTimeout bounds how long writing the response may take.
	WriteTimeout time.Duration `env:"WRITE_TIMEOUT" envDefault:"10s" validate:"required,gt=0"`
	// IdleTimeout bounds how long a keep-alive connection may sit idle
	// between requests.
	IdleTimeout time.Duration `env:"IDLE_TIMEOUT" envDefault:"60s" validate:"required,gt=0"`
	// MaxHeaderBytes bounds the size of request headers, in bytes.
	MaxHeaderBytes int `env:"MAX_HEADER_BYTES" envDefault:"1048576" validate:"min=1"`
	// MaxBodyBytes bounds the size of a request body, in bytes.
	MaxBodyBytes int64 `env:"MAX_BODY_BYTES" envDefault:"2097152" validate:"min=1"`
	// DocumentationEnabled toggles the /docs UI and /openapi.json spec.
	// Recommended false in production to avoid exposing API shape.
	DocumentationEnabled bool `env:"DOCUMENTATION_ENABLED" envDefault:"true"`
	// ShutdownDrainDelay is how long the server waits, still serving
	// traffic, before starting graceful shutdown. It should exceed the
	// time it takes a load balancer or Kubernetes to stop routing new
	// requests to this instance once it is marked not-ready (endpoint
	// propagation delay), so no new request is sent to a server that has
	// already begun to stop. Set to 0 to disable the delay, which is
	// reasonable for local development where there is no load balancer.
	ShutdownDrainDelay time.Duration `env:"SHUTDOWN_DRAIN_DELAY" envDefault:"5s" validate:"min=0"`
}

// Health holds settings for the background dependency health checker in
// internal/foundation/health.
type Health struct {
	// CheckInterval is how often every registered check is run in the
	// background.
	CheckInterval time.Duration `env:"CHECK_INTERVAL" envDefault:"10s" validate:"required,gt=0"`
	// CheckTimeout bounds how long a single check's run may take.
	CheckTimeout time.Duration `env:"CHECK_TIMEOUT" envDefault:"2s" validate:"required,gt=0"`
	// FailureThreshold is how many consecutive failures a check must
	// accumulate before it is marked failing. A single success
	// immediately restores it.
	FailureThreshold int `env:"FAILURE_THRESHOLD" envDefault:"3" validate:"min=1"`
}

// Logging holds settings for the application logger.
type Logging struct {
	// Level is the minimum severity that gets logged.
	Level string `env:"LEVEL" envDefault:"info" validate:"required,oneof=debug info warn error"`
}

// Database holds settings for the connection pool to the application's
// Postgres database (the application role, never the schema-owning
// migration role; see internal/foundation/database/doc.go).
type Database struct {
	// URL is the Postgres connection string for the application role.
	// Treat it as a secret.
	URL string `env:"URL" validate:"required"`
	// MaxConnections bounds how many connections the pool may open.
	MaxConnections int32 `env:"MAX_CONNECTIONS" envDefault:"10" validate:"min=1"`
	// MinConnections is how many connections the pool keeps open, ready,
	// even when idle.
	MinConnections int32 `env:"MIN_CONNECTIONS" envDefault:"2" validate:"min=0"`
	// MaxConnectionLifetime bounds how long a connection may be reused
	// before it is closed and replaced.
	MaxConnectionLifetime time.Duration `env:"MAX_CONNECTION_LIFETIME" envDefault:"30m" validate:"required,gt=0"`
	// MaxConnectionIdleTime bounds how long a connection may sit idle in
	// the pool before it is closed.
	MaxConnectionIdleTime time.Duration `env:"MAX_CONNECTION_IDLE_TIME" envDefault:"5m" validate:"required,gt=0"`
	// ConnectTimeout bounds how long establishing one connection may take.
	ConnectTimeout time.Duration `env:"CONNECT_TIMEOUT" envDefault:"5s" validate:"required,gt=0"`
}

// Provider loads a Configuration from some source (environment variables,
// a file, a secret manager, and so on).
type Provider interface {
	// Load returns a fully populated Configuration, or an error describing
	// why it could not be built.
	Load(ctx context.Context) (Configuration, error)
}
