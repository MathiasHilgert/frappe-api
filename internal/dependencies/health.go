package dependencies

import (
	"context"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/application"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/health"
)

// healthDependencyName identifies the background health checker
// dependency for logging and error reporting.
const healthDependencyName = "health"

// readiness bridges internal/foundation/application and
// internal/foundation/health into the httpserver.Readiness interface,
// since both are foundation leaves that must not import each other: the
// application is ready if and only if every registered hook is up and
// every declared health check is currently passing. checker is set once,
// synchronously, before the returned *application.Application is ever
// started, so no further synchronization is needed to read it.
type readiness struct {
	application *application.Application
	checker     *health.Checker
}

// Ready implements httpserver.Readiness.
func (readiness *readiness) Ready() bool {
	return readiness.application.Ready() && readiness.checker != nil && readiness.checker.Ready()
}

// Report implements httpserver.Readiness, serving the health checker's
// report as the /health/ready response body.
func (readiness *readiness) Report() any {
	if readiness.checker == nil {
		return health.Report{Status: health.StatusFail}
	}
	return readiness.checker.Report()
}

// adaptChecks translates application.Check values, collected from every
// dependency that declared one, into health.Check values understood by
// the health package.
func adaptChecks(applicationChecks []application.Check) []health.Check {
	checks := make([]health.Check, 0, len(applicationChecks))
	for _, check := range applicationChecks {
		checks = append(checks, health.Check{Name: check.Name, Run: check.Run})
	}
	return checks
}

// provideHealthChecker registers the background health.Checker as a
// dependency and returns it. It must be called after every other Provide
// call in this package that might declare a Check, so the checker
// observes the full set of checks collected on instance.
func provideHealthChecker(instance *application.Application, config health.Config) *health.Checker {
	checker := health.NewChecker(adaptChecks(instance.Checks()), config)

	application.Provide(instance, application.Dependency[*health.Checker]{
		Name: healthDependencyName,
		Up: func(ctx context.Context) (*health.Checker, error) {
			return checker, checker.Start(ctx)
		},
		Down: func(ctx context.Context, checker *health.Checker) error {
			return checker.Stop(ctx)
		},
	})

	return checker
}
