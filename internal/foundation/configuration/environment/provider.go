// Package environment implements configuration.Provider by reading
// environment variables through github.com/caarlos0/env, then validating
// the result with configuration.Validate.
package environment

import (
	"context"
	"fmt"

	"github.com/caarlos0/env/v11"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
)

// Provider loads configuration.Configuration from environment variables.
type Provider struct {
	// variables holds the injected environment to read from. When nil,
	// Load falls back to the current process environment.
	variables map[string]string
}

// Option customizes a Provider built by New.
type Option func(*Provider)

// WithVariables injects the environment map Load reads from, instead of
// the current process environment. It exists so tests can exercise
// Provider without depending on ambient process state.
func WithVariables(variables map[string]string) Option {
	return func(provider *Provider) {
		provider.variables = variables
	}
}

// New creates an environment-variable configuration.Provider. By default
// it reads from the current process environment; pass WithVariables to
// read from an injected map instead.
func New(options ...Option) Provider {
	provider := Provider{}
	for _, option := range options {
		option(&provider)
	}
	return provider
}

// Load parses configuration.Configuration from the provider's environment
// (the injected map, or the current process environment by default),
// applying defaults declared through envDefault tags, and then validates
// the result. It returns a wrapped error identifying the failing
// environment variable if parsing fails; validation failures are wrapped
// configuration.ValidationError values, so errors.As can retrieve the
// per-variable violations.
func (provider Provider) Load(_ context.Context) (configuration.Configuration, error) {
	var loadedConfiguration configuration.Configuration

	parseOptions := env.Options{}
	if provider.variables != nil {
		parseOptions.Environment = provider.variables
	}

	if err := env.ParseWithOptions(&loadedConfiguration, parseOptions); err != nil {
		return configuration.Configuration{}, fmt.Errorf("parse configuration from environment: %w", err)
	}

	if err := configuration.Validate(loadedConfiguration); err != nil {
		return configuration.Configuration{}, fmt.Errorf("validate configuration: %w", err)
	}

	return loadedConfiguration, nil
}
