package dependencies

import (
	"testing"
	"time"

	"github.com/jackc/pgx/v5/pgxpool"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/application"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/jobs"
)

func TestProvideJobsRegistersTheBackendWithAHealthCheck(t *testing.T) {
	instance := application.New()
	provideJobs(instance, configuration.Jobs{Enabled: true}, jobs.NewCatalog(), &application.Handle[*pgxpool.Pool]{})
	checks := instance.Checks()
	if len(checks) != 1 || checks[0].Name != jobsDependencyName {
		t.Fatalf("got %d checks, want one %q check", len(checks), jobsDependencyName)
	}
}

func TestJobsSettingsFromMapsEveryField(t *testing.T) {
	settings := jobsSettingsFrom(configuration.Jobs{
		Workers:            7,
		MaxAttempts:        4,
		FetchPollInterval:  2 * time.Second,
		JobTimeout:         3 * time.Minute,
		CompletedRetention: 5 * time.Hour,
		MetricsInterval:    6 * time.Second,
	})
	if settings.Workers != 7 || settings.MaxAttempts != 4 || settings.FetchPollInterval != 2*time.Second ||
		settings.JobTimeout != 3*time.Minute || settings.CompletedRetention != 5*time.Hour || settings.MetricsInterval != 6*time.Second {
		t.Fatalf("settings = %+v", settings)
	}
}
