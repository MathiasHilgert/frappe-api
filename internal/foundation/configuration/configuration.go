// Package configuration declares the application-wide Configuration
// structure and the Provider port used to load it. Concrete providers
// (for example an environment-variable provider) live in subpackages and
// implement Provider.
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
