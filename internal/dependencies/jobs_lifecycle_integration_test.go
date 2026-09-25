//go:build integration

package dependencies

import (
	"context"
	"testing"
	"time"

	"github.com/jackc/pgx/v5/pgxpool"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/application"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/database/databasetest"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/jobs"
)

type lifecycleArguments struct{}

// TestIntegrationJobsRunAfterUpAndDrainOnDown drives the jobs backend
// through the real application lifecycle: Up runs every hook on a phase
// context that is cancelled once Up returns, so jobs must still run
// afterwards; Down drains in-flight handlers, which may still enqueue.
func TestIntegrationJobsRunAfterUpAndDrainOnDown(t *testing.T) {
	const eventually = 30 * time.Second
	pool := databasetest.New(t)
	instance := application.New(application.WithHookTimeout(10 * time.Second))
	poolHandle := application.Provide(instance, application.Dependency[*pgxpool.Pool]{
		Name: "database",
		Up:   func(context.Context) (*pgxpool.Pool, error) { return pool, nil },
	})

	catalog := jobs.NewCatalog()
	module := catalog.Module("orders")
	afterUp := jobs.Define[lifecycleArguments](module, "after_up")
	draining := jobs.Define[lifecycleArguments](module, "draining")
	followUp := jobs.Define[lifecycleArguments](module, "follow_up")

	ranAfterUp := make(chan struct{}, 1)
	jobs.Handle(module, afterUp, func(context.Context, jobs.Job[lifecycleArguments]) error {
		ranAfterUp <- struct{}{}
		return nil
	})
	drainStarted := make(chan struct{})
	release := make(chan struct{})
	followUpResult := make(chan error, 1)
	jobs.Handle(module, draining, func(ctx context.Context, _ jobs.Job[lifecycleArguments]) error {
		close(drainStarted)
		<-release
		_, err := followUp.Enqueue(ctx, lifecycleArguments{})
		followUpResult <- err
		return nil
	})
	jobs.Handle(module, followUp, func(context.Context, jobs.Job[lifecycleArguments]) error { return nil })

	provideJobs(instance, configuration.Jobs{
		Enabled:            true,
		Workers:            2,
		MaxAttempts:        3,
		FetchPollInterval:  100 * time.Millisecond,
		JobTimeout:         time.Minute,
		CompletedRetention: time.Hour,
		MetricsInterval:    time.Second,
	}, catalog, poolHandle)

	if err := instance.Up(context.Background()); err != nil {
		t.Fatalf("Up() = %v", err)
	}

	if _, err := afterUp.Enqueue(context.Background(), lifecycleArguments{}); err != nil {
		t.Fatal(err)
	}
	select {
	case <-ranAfterUp:
	case <-time.After(eventually):
		t.Fatal("no job ran after Up returned: the backend died with the Up phase context")
	}

	if _, err := draining.Enqueue(context.Background(), lifecycleArguments{}); err != nil {
		t.Fatal(err)
	}
	select {
	case <-drainStarted:
	case <-time.After(eventually):
		t.Fatal("the draining job never started")
	}
	downResult := make(chan error, 1)
	go func() { downResult <- instance.Down(context.Background()) }()
	time.Sleep(300 * time.Millisecond)
	close(release)

	if err := <-followUpResult; err != nil {
		t.Fatalf("enqueue from a handler while draining = %v, want nil", err)
	}
	if err := <-downResult; err != nil {
		t.Fatalf("Down() = %v", err)
	}
	var count int
	if err := pool.QueryRow(context.Background(), "SELECT count(*) FROM river_job WHERE kind = $1", followUp.Name()).Scan(&count); err != nil {
		t.Fatal(err)
	}
	if count != 1 {
		t.Fatalf("follow-up jobs = %d, want 1", count)
	}
}
