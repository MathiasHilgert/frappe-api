package dependencies

import (
	"context"

	"github.com/jackc/pgx/v5/pgxpool"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/application"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/jobs"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/jobs/river"
)

// jobsDependencyName identifies the jobs backend dependency.
const jobsDependencyName = "jobs"

// jobsTenancy captures and restores the tenant of jobs. The tenant model
// is not defined yet, so it is empty and jobs carry no tenant; wire the
// same resolver as the cache and a binder feeding the Row Level Security
// setting here once it exists.
var jobsTenancy jobs.Tenancy

// provideJobs registers the River jobs backend for catalog on the
// application pool. At Up it validates the catalog (every job has a
// handler), installs the backend as the catalog's enqueuer and, when
// JOBS_ENABLED is true, starts working jobs and joining the periodic
// leader election. Disabled, the replica still enqueues. Its health check
// reports whether the job tables are reachable.
func provideJobs(instance *application.Application, settings configuration.Jobs, catalog *jobs.Catalog, pool *application.Handle[*pgxpool.Pool]) {
	catalog.UseTenancy(jobsTenancy)
	application.Provide(instance, application.Dependency[*river.Client]{
		Name: jobsDependencyName,
		Up: func(ctx context.Context) (*river.Client, error) {
			applicationPool, ready := pool.Get()
			if !ready || applicationPool == nil {
				return nil, errDatabaseNotReady
			}
			client, err := river.New(applicationPool, catalog, jobsSettingsFrom(settings))
			if err != nil {
				return nil, err
			}
			var enqueuer jobs.Enqueuer = client
			catalog.Use(enqueuer)
			if settings.Enabled {
				if err := client.Start(ctx); err != nil {
					catalog.Use(nil)
					return nil, err
				}
			}
			return client, nil
		},
		// Down only stops working: handlers still draining (and any other
		// component still going down) keep enqueuing, which inserts on the
		// pool without a started client. The pool closes after this hook.
		Down: func(ctx context.Context, client *river.Client) error {
			return client.Stop(ctx)
		},
		Check: func(ctx context.Context, client *river.Client) error {
			return client.Check(ctx)
		},
	})
}

// jobsSettingsFrom maps JOBS_* onto the River backend settings.
func jobsSettingsFrom(settings configuration.Jobs) river.Settings {
	return river.Settings{
		FetchPollInterval:  settings.FetchPollInterval,
		JobTimeout:         settings.JobTimeout,
		CompletedRetention: settings.CompletedRetention,
		MetricsInterval:    settings.MetricsInterval,
		Workers:            settings.Workers,
		MaxAttempts:        settings.MaxAttempts,
	}
}
