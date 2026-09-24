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
type Provider struct{}

// New creates an environment-variable configuration.Provider.
func New() Provider {
	return Provider{}
}

// Load parses configuration.Configuration from the current process
// environment, applying defaults declared through envDefault tags, and
// then validates the result. It returns a wrapped error identifying the
// failing environment variable if parsing or validation fails.
func (Provider) Load(_ context.Context) (configuration.Configuration, error) {
	var loadedConfiguration configuration.Configuration

	if err := env.Parse(&loadedConfiguration); err != nil {
		return configuration.Configuration{}, fmt.Errorf("parse configuration from environment: %w", err)
	}

	if err := configuration.Validate(loadedConfiguration); err != nil {
		return configuration.Configuration{}, err
	}

	return loadedConfiguration, nil
}
