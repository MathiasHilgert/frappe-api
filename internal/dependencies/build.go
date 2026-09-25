package dependencies

import (
	"context"
	"fmt"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/application"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
)

// NewApplication loads the application-wide configuration through
// provider and builds the Application, wiring every concrete module into
// it. It is the single place that knows the full set of modules the
// running program uses.
func NewApplication(ctx context.Context, provider configuration.Provider) (*application.Application, error) {
	loadedConfiguration, err := provider.Load(ctx)
	if err != nil {
		return nil, fmt.Errorf("load configuration: %w", err)
	}

	instance := application.New(application.WithHookTimeout(loadedConfiguration.Application.HookTimeout))

	// No concrete module exists yet; modules will be registered here with
	// instance.Use(...) as they are added.

	return instance, nil
}
