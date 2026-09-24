package dependencies

import "github.com/MathiasHilgert/frappe-api/internal/foundation/application"

// NewApplication builds the Application, wiring every concrete module into
// it. It is the single place that knows the full set of modules the
// running program uses.
func NewApplication() *application.Application {
	application_ := application.New()

	// No concrete module exists yet; modules will be registered here with
	// application_.Use(...) as they are added.

	return application_
}
