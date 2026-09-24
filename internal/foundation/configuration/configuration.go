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
	HTTP        HTTP        `envPrefix:"HTTP_"`
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
	// Version identifies the running build of the program in telemetry.
	Version string `env:"VERSION" envDefault:"0.0.0" validate:"required"`
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
}

// Logging holds settings for the application logger.
type Logging struct {
	// Level is the minimum severity that gets logged.
	Level string `env:"LEVEL" envDefault:"info" validate:"required,oneof=debug info warn error"`
}

// Provider loads a Configuration from some source (environment variables,
// a file, a secret manager, and so on).
type Provider interface {
	// Load returns a fully populated Configuration, or an error describing
	// why it could not be built.
	Load(ctx context.Context) (Configuration, error)
}
