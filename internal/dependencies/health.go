package dependencies

import (
	"context"
	"fmt"

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
// every declared health check is currently passing. checker is a Handle
// because the *health.Checker it exposes is built lazily inside
// provideHealthChecker's Up function (see its own doc comment), so it is
// only ready once that Up has run; Handle.Get already documents that this
// makes it safe to read without further synchronization.
type readiness struct {
	application *application.Application
	checker     *application.Handle[*health.Checker]
}

// Ready implements httpserver.Readiness.
func (readiness *readiness) Ready() bool {
	checker, ready := readiness.checker.Get()
	return readiness.application.Ready() && ready && checker.Ready()
}

// Report implements httpserver.Readiness, serving the health checker's
// report as the /health/ready response body. The overall status is forced
// to fail while the application lifecycle is not ready (starting up or
// draining), so the body always agrees with the 503 status code.
func (readiness *readiness) Report() any {
	checker, ready := readiness.checker.Get()
	if !ready {
		return health.Report{Status: health.StatusFail}
	}
	report := checker.Report()
	if !readiness.application.Ready() {
		report.Status = health.StatusFail
	}
	return report
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
// dependency. Unlike every other dependency in this package, the
// *health.Checker itself is built lazily, inside Up, by reading
// instance.Checks() at that point rather than when provideHealthChecker
// is called: this means the order provideHealthChecker is called in,
// relative to every other Provide call in this package, no longer
// matters, and a dependency wired in after the health checker still gets
// checked.
func provideHealthChecker(instance *application.Application, settings health.Settings) *application.Handle[*health.Checker] {
	return application.Provide(instance, application.Dependency[*health.Checker]{
		Name: healthDependencyName,
		Up: func(ctx context.Context) (*health.Checker, error) {
			checker, err := health.NewChecker(adaptChecks(instance.Checks()), settings)
			if err != nil {
				return nil, fmt.Errorf("health: build checker: %w", err)
			}
			if err := checker.Start(ctx); err != nil {
				return nil, err
			}
			return checker, nil
		},
		Down: func(ctx context.Context, checker *health.Checker) error {
			return checker.Stop(ctx)
		},
	})
}
