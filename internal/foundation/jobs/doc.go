// Package jobs is the backend-agnostic background and periodic jobs
// platform, the twin of the events package: typed job definitions scoped
// to a module, the Enqueuer port, typed handlers, periodic schedules and
// their telemetry. The production backend is jobs/river (River on
// Postgres); modules never import it.
//
// # Wire
//
// The composition root creates one Catalog, the counterpart of
// events.NewRegistry, and hands catalog.Module("<module>") to each module,
// like registry.Module("<module>"). It installs the backend with
// catalog.Use and passes the catalog to the backend, which validates it at
// startup.
//
// # Define
//
// Jobs are private to the module that owns them. A module defines them on
// its Module; the name is <module>.<action>, both lowercase snake_case,
// validated and unique per catalog:
//
//	type RebuildIndexArgs struct {
//		MenuID uuid.UUID `json:"menuId"`
//	}
//
//	type Jobs struct {
//		RebuildIndex *jobs.Definition[RebuildIndexArgs]
//	}
//
//	func DefineJobs(module *jobs.Module) Jobs {
//		return Jobs{RebuildIndex: jobs.Define[RebuildIndexArgs](module, "rebuild_index",
//			jobs.WithQueue("maintenance"), jobs.WithMaxAttempts(5), jobs.WithTimeout(time.Minute))}
//	}
//
// Arguments carry identifiers, never entities: a job may run more than once
// and long after it was enqueued, so handlers load current state and must
// be idempotent.
//
// # Enqueue
//
// Use cases receive the definitions they enqueue through their module
// Dependencies:
//
//	receipt, err := useCase.jobs.RebuildIndex.Enqueue(ctx, RebuildIndexArgs{MenuID: id},
//		jobs.After(time.Minute), jobs.Queue("bulk"), jobs.Unique(time.Hour))
//
// Inside database.WithinTransaction the job is inserted in that
// transaction: it exists if and only if the unit of work commits. Enqueue
// opens a producer span and carries its trace context, and the tenant
// resolved by the catalog's Tenancy, in the job metadata. Unique with a
// zero period deduplicates against unfinished jobs only; a positive period
// deduplicates within that time bucket.
//
// # Handle and schedule
//
// The module registers the handler, and any schedule, at wiring time:
//
//	jobs.Handle(module, moduleJobs.RebuildIndex, func(ctx context.Context, job jobs.Job[RebuildIndexArgs]) error {
//		return useCase.Execute(ctx, job.Args.MenuID)
//	})
//	jobs.Every(moduleJobs.RebuildIndex, 15*time.Minute, RebuildIndexArgs{})
//	jobs.Cron(moduleJobs.RebuildIndex, "0 3 * * *", RebuildIndexArgs{})
//
// Returning nil completes the job; an error retries it with backoff until
// the attempts run out; jobs.Cancel stops it for good; jobs.Snooze runs it
// again later without consuming an attempt. Schedules tick once across
// every replica. The handler runs with the enqueuing tenant restored, in a
// consumer span linked to the producer span, and records
// frappe.jobs.handled, frappe.jobs.handle.duration, frappe.jobs.attempts
// and frappe.jobs.lag plus one structured log line per attempt.
//
// # Differences from events, on purpose
//
//   - Definitions are built on a Module, not declared globally with a
//     full name: jobs are private to their module, so the module scope
//     owns the name (<module>.<action>) and a job can only be handled by
//     the module that defined it (Handle panics otherwise), whereas an
//     event is published language any module may consume.
//   - A definition enqueues itself (Definition.Enqueue) instead of going
//     through an injected Recorder: the definition already carries its
//     catalog, whose enqueuer the composition root installs.
//   - Names carry no version: arguments evolve compatibly inside one
//     module, while events cross module boundaries and are versioned.
//
// # Test
//
// jobstest.NewCatalog returns an isolated catalog whose enqueuer is a
// Recorder; jobstest.Run executes a handler synchronously; backends prove
// the contract with queuetest.Run.
package jobs
