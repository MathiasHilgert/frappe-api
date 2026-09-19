# FAPI-10 — platform: Run cluster-safe scheduled tasks with db-scheduler

Plane: [FAPI-10](https://app.plane.so/nulled-software/browse/FAPI-10/) (module platform, size M). Branch: `feat/fapi-10-db-scheduler`. Worktree: `~/Projects/nulled/frappe-api-worktrees/FAPI-10`.

## Objective
Time-based work runs exactly once per due execution across all instances, inside the app, on our Postgres: db-scheduler with its Spring Boot 4 starter, platform conventions for declaring tasks, every execution observed, and outbox recovery migrated off `@Scheduled` and its in-process lock.

## Decisions (from the ticket)
- db-scheduler plus its Spring Boot 4 starter, latest release on Maven Central (verified in T0, never guessed).
- Table `platform.scheduled_tasks`, created by a Flyway migration in `db/migration/platform` (schema-qualified, versioned after the existing ones), owned by `frappe_owner`, DML for `frappe_app` through the default privileges. The library never creates tables at runtime.
- Platform conventions: fixed recurring tasks, one-time tasks, recurring tasks with a persistent per-entity schedule (cron + `ZoneId`); retries with exponential backoff; natural-key task instance ids for uniqueness.
- Every execution observed: Micrometer Observation `scheduled.task` (span + timer), low-cardinality task name and outcome; failures logged with ECS fields.
- Outbox recovery runs through db-scheduler; the in-process lock and `@EnableScheduling` go.
- `writing-code` documents how a module declares a task.
- Properties under `db-scheduler.*` and `frappe.scheduling.*`. Business metrics: none (infrastructure telemetry via `scheduled.task`).
- Shared files (`build.gradle.kts`, `application*.properties`, `compose.yaml`) are edited minimally and locally: parallel tickets (FAPI-11, 12, 13, 16) touch them too.

## Out of scope
Archive purge (FAPI-9), per-branch business-day close, unverified-account purge (their own tickets use this), a scheduler dashboard (Grafana).

## TDD
Strict TDD (brief and project rule). Runner: `./gradlew test` with `FRAPPE_TEST_DB=frappe_fapi_10` (Testcontainers Postgres, NATS per context). RED observed before every behavior; evidence below.

## Tasks
- [ ] T0 Verify db-scheduler and its Boot 4 starter (versions, auto-configuration, APIs, DDL) from Maven Central sources jars; record findings and design here
- [ ] T1 Dependency, Flyway migration `platform.scheduled_tasks`, `db-scheduler.*` settings; the app's scheduler runs on the Flyway-owned table
- [ ] T2 Task conventions: `ScheduledTasks` (recurring, one-time, per-entity), `EntitySchedule`, `frappe.scheduling.*` retry settings with exponential backoff, task name rule
- [ ] T3 Observation `scheduled.task` and failure logging (ECS fields); JSON task data; pausing the scheduler with the application context
- [ ] T4 Acceptance: two schedulers racing on real Postgres (exactly once, dead instance taken over, retries with backoff observed as errors, per-entity schedule change without restart)
- [ ] T5 Outbox recovery through db-scheduler; remove the lock and `@EnableScheduling`
- [ ] T6 Docs (`writing-code`: declaring a task; outbox recovery, observability, errors), final verification

## Acceptance (from the ticket)
- Two application instances, a recurring task due → exactly one executes it.
- An instance dies mid-execution → after the heartbeat expires another instance picks the execution up.
- A task fails → it retries with exponential backoff and each failure is observed as an error.
- A per-entity schedule changes (e.g. a new timezone) → the next execution follows it without restart.
- Outbox recovery runs through db-scheduler and no in-process lock remains.

## Checks
`FRAPPE_TEST_DB=frappe_fapi_10 ./gradlew spotlessApply check --rerun-tasks`.

## Progress / evidence

## Next step
T0.
