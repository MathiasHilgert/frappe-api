package dependencies

import (
	"testing"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/application"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/health"
)

func TestReadinessReportFailsWhenApplicationIsNotReady(t *testing.T) {
	subject := &readiness{
		application: application.New(),
		checker:     health.NewChecker(nil, health.Settings{}),
	}

	report, ok := subject.Report().(health.Report)
	if !ok {
		t.Fatalf("Report() type = %T, want health.Report", subject.Report())
	}
	if report.Status != health.StatusFail {
		t.Errorf("Report().Status = %q, want %q while the application is not ready", report.Status, health.StatusFail)
	}
}
