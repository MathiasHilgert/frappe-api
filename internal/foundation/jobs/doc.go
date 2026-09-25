// Package jobs is the backend-agnostic background and periodic jobs
// platform, the twin of the events package: typed job definitions scoped
// to a module, the Enqueuer port, typed handlers, periodic schedules and
// their telemetry. The production backend is jobs/river (River on
// Postgres); modules never import it.
//
// # Define
//
// Jobs are private to the module that owns them. A module declares them on
// its scope of the default catalog; the name is <module>.<action>, both
// lowercase snake_case, validated and unique at startup:
//
//	var module = jobs.For("menu")
//
//	type RebuildIndexArgs struct {
//		MenuID uuid.UUID `json:"menuId"`
//	}
//
//	var RebuildIndex = jobs.Define[RebuildIndexArgs](module, "rebuild_index",
//		jobs.WithQueue("maintenance"), jobs.WithMaxAttempts(5), jobs.WithTimeout(time.Minute))
//
// Arguments carry identifiers, never entities: a job may run more than once
// and long after it was enqueued, so handlers load current state and must
// be idempotent.
//
// # Enqueue
//
//	receipt, err := RebuildIndex.Enqueue(ctx, RebuildIndexArgs{MenuID: id},
//		jobs.After(time.Minute), jobs.Queue("bulk"), jobs.Unique(time.Hour))
//
// Inside database.WithinTransaction the job is inserted in that
// transaction: it exists if and only if the unit of work commits. Enqueue
// opens a producer span and carries its trace context, and the tenant
// resolved by the catalog's Tenancy, in the job metadata.
//
// # Handle and schedule
//
// The module root registers the handler, and any schedule, at wiring time:
//
//	jobs.Handle(RebuildIndex, func(ctx context.Context, job jobs.Job[RebuildIndexArgs]) error {
//		return useCase.Execute(ctx, job.Args.MenuID)
//	})
//	jobs.Every(RebuildIndex, 15*time.Minute, RebuildIndexArgs{})
//	jobs.Cron(RebuildIndex, "0 3 * * *", RebuildIndexArgs{})
//
// Returning nil completes the job; an error retries it with backoff until
// the attempts run out; jobs.Cancel stops it for good; jobs.Snooze runs it
// again later without consuming an attempt. Schedules tick once across
// every replica. The handler runs with the enqueuing tenant restored, in a
// consumer span linked to the producer span, and records
// frappe.jobs.handled, frappe.jobs.handle.duration, frappe.jobs.attempts
// and frappe.jobs.lag plus one structured log line per attempt.
//
// # Test
//
// jobstest.NewRecorder captures enqueued jobs through the context and
// jobstest.Run executes a handler synchronously; backends prove the
// contract with queuetest.Run.
package jobs
