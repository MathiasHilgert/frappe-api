package dependencies

import (
	"context"
	"testing"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/application"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/health"
)

func TestReadinessReportFailsWhenApplicationIsNotReady(t *testing.T) {
	lifecycle := application.New()
	handle := application.Provide(lifecycle, application.Dependency[*health.Checker]{
		Name: "health",
		Up: func(context.Context) (*health.Checker, error) {
			return health.NewChecker(nil, health.Settings{})
		},
	})
	if err := lifecycle.Up(context.Background()); err != nil {
		t.Fatalf("Up returned unexpected error: %v", err)
	}

	subject := &readiness{
		application: application.New(),
		checker:     handle,
	}

	report, ok := subject.Report().(health.Report)
	if !ok {
		t.Fatalf("Report() type = %T, want health.Report", subject.Report())
	}
	if report.Status != health.StatusFail {
		t.Errorf("Report().Status = %q, want %q while the application is not ready", report.Status, health.StatusFail)
	}
}

// TestProvideHealthCheckerObservesADependencyDeclaredAfterIt verifies
// that provideHealthChecker builds its *health.Checker lazily, at Up
// time, so a dependency registered after provideHealthChecker was called
// is still included in the checks it observes.
func TestProvideHealthCheckerObservesADependencyDeclaredAfterIt(t *testing.T) {
	instance := application.New()

	handle := provideHealthChecker(instance, health.Settings{})

	application.Provide(instance, application.Dependency[string]{
		Name: "declared-after-the-checker",
		Up: func(context.Context) (string, error) {
			return "value", nil
		},
		Check: func(context.Context, string) error {
			return nil
		},
	})

	if err := instance.Up(context.Background()); err != nil {
		t.Fatalf("Up returned unexpected error: %v", err)
	}
	defer func() { _ = instance.Down(context.Background()) }()

	checker, ready := handle.Get()
	if !ready {
		t.Fatal("checker handle is not ready after Up")
	}

	if _, ok := checker.Report().Checks["declared-after-the-checker"]; !ok {
		t.Fatal("checker report is missing the dependency declared after provideHealthChecker was called")
	}
}
