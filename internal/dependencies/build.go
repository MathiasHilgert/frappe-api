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
	config, err := provider.Load(ctx)
	if err != nil {
		return nil, fmt.Errorf("load configuration: %w", err)
	}

	application_ := application.New(application.WithHookTimeout(config.HTTP.ShutdownTimeout))

	// No concrete module exists yet; modules will be registered here with
	// application_.Use(...) as they are added.

	return application_, nil
}
