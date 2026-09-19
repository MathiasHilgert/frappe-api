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
- [x] T0 Verify db-scheduler and its Boot 4 starter (versions, auto-configuration, APIs, DDL) from Maven Central sources jars; record findings and design here
- [x] T1 Dependency, Flyway migration `platform.scheduled_tasks`, `db-scheduler.*` settings; the app's scheduler runs on the Flyway-owned table
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

### T0 findings (Maven Central `maven-metadata.xml`, POMs and `-sources.jar` of 16.12.0; official DDL from the `v16.12.0` tag)
- Latest release: `com.github.kagkarlsson:db-scheduler` **16.12.0** (metadata `<release>16.12.0</release>`, 2026-05-29). The Boot 4 starter is a separate artifact, `com.github.kagkarlsson:db-scheduler-spring-boot-4-starter` **16.12.0** (`db-scheduler-spring-boot-starter` is the Boot 3 one). It depends on `db-scheduler`, `db-scheduler-spring-common`, `spring-boot`, `spring-boot-sql`, `spring-boot-jdbc`, `spring-boot-autoconfigure`, `micrometer-core`, Jackson 3 (`tools.jackson.core`); metrics and health modules optional. Built against Boot 4.0.6; every Boot class the starter imports (17) exists in our Boot 4.1.1 jars (checked class by class).
- Auto-configuration (`DbSchedulerAutoConfiguration`, `@ConditionalOnBean(DataSource)`, `db-scheduler.enabled`): collects every `Task<?>` bean, every `SchedulerListener` and `ExecutionInterceptor` bean; `DbSchedulerCustomizer` bean supplies serializer, executors, scheduler name; `Scheduler` bean with `destroyMethod = "stop"` and `@DependsOnDatabaseInitialization` (Flyway first); `DbSchedulerStarter` starts it immediately or, with `db-scheduler.delay-startup-until-context-ready=true`, on `ContextRefreshedEvent`. Wraps the `DataSource` in `TransactionAwareDataSourceProxy`, so scheduling from inside a Spring transaction commits with it. `DbSchedulerMetricsAutoConfiguration` registers `MicrometerStatsRegistry` (library meters) when a `MeterRegistry` exists; `DbSchedulerActuatorAutoConfiguration` a `db-scheduler` health indicator.
- `DbSchedulerProperties` (`db-scheduler.*`): `table-name` (default `scheduled_tasks`, used verbatim in SQL, so `platform.scheduled_tasks` works), `polling-interval` 10s, `heartbeat-interval` 5m, `missed-heartbeats-limit` 6 (dead after 30m), `shutdown-max-wait` 30m (waited up to twice), `polling-strategy` FETCH, `failure-logger-level` DEBUG, `threads` 10.
- **No DDL anywhere** in the three source jars (`create table|create index|alter table`: no match): the library never creates tables; the table comes only from Flyway. Official Postgres DDL (`db-scheduler/src/test/resources/postgresql_tables.sql` at `v16.12.0`): columns `task_name, task_instance, task_data bytea, execution_time timestamptz, picked, picked_by, last_success, last_failure, consecutive_failures, last_heartbeat, version bigint, priority smallint`, pk `(task_name, task_instance)`, indexes on `execution_time`, `last_heartbeat`, `(priority desc, execution_time)`.
- Default task-data serializer is Java serialization (`DbSchedulerConfigurationSupport.SPRING_JAVA_SERIALIZER`); the starter ships `Jackson3Serializer` (Jackson 3, `ScheduleMixin` for polymorphic `Schedule`) but does not use it unless a customizer returns it.
- Task API (`Tasks`): `recurring(name, Schedule[, dataClass])` (default failure: reschedule per schedule; startup: `ScheduleRecurringOnStartup`, instance id `recurring`, reschedules on startup when a deterministic schedule changed or a fixed delay got shorter), `recurringWithPersistentSchedule(name, dataClass extends ScheduleAndData)` (schedule stored in the task data per instance), `oneTime(name, dataClass)` (default failure: retry every 5 min). Dead executions default to `ReviveDeadExecution` (rescheduled to now).
- Failure handling: `FailureHandler.maxRetries(n).withBackoff(initial, multiplier).then(handler, MaxRetriesExceededListener)`; the backoff is `initial × multiplier^consecutiveFailures` (`ExponentialBackoffFailureHandler`, no cap, hence the retry limit), `then(...)` delegates once `consecutiveFailures + 1 > n`. `OnFailureReschedule(schedule)`, `OnFailureRescheduleUsingTaskDataSchedule`, `OnFailureRetryLater(delay)` exist.
- `ExecutionInterceptor.execute(taskInstance, context, chain)` wraps the handler call on the executing thread (`ExecutePicked`); a thrown exception passes through the chain and is then handed to the task's `FailureHandler`. `SchedulerListener.onExecutionComplete(ExecutionComplete)` (`getResult()` OK/FAILED, `getCause()`, `getExecution().consecutiveFailures` before this failure) runs after the completion/failure handler on the same thread; `AbstractSchedulerListener` has empty defaults.
- `SchedulerClient`: `scheduleIfNotExists(instance, time)` (natural-key dedup on the primary key), `schedule(schedulableInstance, ScheduleOptions.WHEN_EXISTS_RESCHEDULE)` (upsert incl. data), `reschedule(id, time, data)` (throws `TaskInstanceCurrentlyExecutingException` while picked, `TaskInstanceNotFoundException`; version race returns false). `Scheduler.triggerCheckForDueExecutions()` wakes this instance's poller. SQL failures surface as the shaded `com.github.kagkarlsson.shaded.jdbc.SQLRuntimeException`, library faults as `DbSchedulerException` subtypes.
- `Scheduler.pause()/resume()` only stop picking due executions (`RunUntilShutdown`); heartbeats and dead-execution detection continue. `Scheduler.stop()` is terminal.
- `CronSchedule(pattern, zone)` parses Spring 5.3 style cron (six fields, seconds first) at construction (shaded cron-utils), rejecting invalid patterns.
- Spring Framework 7.0.9 (`DefaultContextCache`): cached test contexts are paused on a context switch (`ConfigurableApplicationContext.pause()` stops pauseable `Lifecycle` beans) and restarted when reused.

### Design (T0)
- `platform.infrastructure.scheduling` wires the starter: `DbSchedulerCustomizer` with `Jackson3Serializer` (readable, evolvable task data instead of Java serialization), the `scheduled.task` `ExecutionInterceptor`, a `SchedulerListener` that logs failures once, and a `SmartLifecycle` that pauses picking when the context stops (graceful shutdown stops taking new work before `Scheduler.stop()` drains; cached test contexts no longer run each other's tasks, as `@Scheduled` executors were paused before).
- Public kernel API in `com.frappe.platform`: `ScheduledTasks` (bean) builds tasks with the conventions (task name `<module>.<kebab-name>`, exponential backoff from `frappe.scheduling.*`, then fall back to the schedule for recurring tasks and keep retrying at the last backoff for one-time tasks, so no work is lost) and `EntitySchedule(cron, zone)` for per-entity schedules. Modules declare tasks as beans in their `infrastructure` and schedule instances through `SchedulerClient` with natural-key instance ids.
- Outbox recovery: one recurring task `platform.outbox-recovery` whose data is the pass trigger. A recovered transport reschedules that single execution to now with trigger `TRANSPORT_RECOVERED` and wakes the poller, so passes never overlap anywhere in the cluster (no lock); a pass already running covers the same rows.
- Settings: `db-scheduler.table-name=platform.scheduled_tasks`, `delay-startup-until-context-ready=true`, `shutdown-max-wait=10s` (the 30m default outlives any rolling deploy; unfinished executions are taken over after the heartbeat expires). Priority is not enabled, so its index is left out.

### T1 table and settings
- RED 1 (compilation) `SchedulerTableIntegrationTests` (`schedulesIntoTheFlywayOwnedPlatformTable`, `schedulingTheSameNaturalKeyTwiceKeepsOneExecution`): `package com.github.kagkarlsson.scheduler does not exist`.
- RED 2 after adding `db-scheduler-spring-boot-4-starter:16.12.0`: both fail with `SQLRuntimeException` caused by `PSQLException: ERROR: relation "scheduled_tasks" does not exist`; `FlywayMigrationIntegrationTests.startupCreatesTheSchedulerTableOwnedByOwnerRole` (`EmptyResultDataAccessException`) and `schedulerTableCarriesTheIndexesOfItsPollingQueries` (`AssertionError`) fail too.
- GREEN: migration `V202609191000__create_scheduled_tasks.sql` (official DDL qualified with `platform`, priority index left out) and `db-scheduler.table-name`, `delay-startup-until-context-ready`, `shutdown-max-wait` in `application.properties`: 2/2 and 7/7. The runtime role schedules into the table with the default privileges, no grant in the migration.
- `FRAPPE_TEST_DB=frappe_fapi_10 ./gradlew spotlessApply check`: BUILD SUCCESSFUL.

## Next step
T2.
