# Scheduled tasks

Time-based work (purges, a branch's business-day close, reports, outbox recovery) runs as cluster-safe scheduled tasks inside the app, on our Postgres: every due execution runs on exactly one instance, and one that was running on an instance that died is taken over once its heartbeat expires. Reactive work stays on domain events and the outbox. Never use `@Scheduled`, `@EnableScheduling`, a `TaskScheduler`, an in-process lock or Spring Modulith Moments (`spring.modulith.moments.enabled=false`): each of them runs once per instance.

The engine is db-scheduler 16.12.0, an implementation detail of `platform.infrastructure.scheduling`: modules use only the kernel types in `com.frappe.platform` (`ScheduledTasks`, `TaskName`, `TaskSchedule`, `EntitySchedule`, `RecurringTask`, `OneTimeTask`, `EntityTask`, `TaskSchedulingException`). `SchedulingIsolationTest` fails the build if any other package imports `com.github.kagkarlsson`.

## Declaring a task

A task is a bean in the module's `infrastructure`, created with `ScheduledTasks`; the platform builds the scheduler from every declared task bean. The action calls the module's bus or a port, like a controller does; it holds no business logic.

```java
@Configuration(proxyBeanMethods = false)
class IdentityTasks {

    // Fixed recurring: one execution for the whole cluster, scheduled at startup.
    @Bean
    RecurringTask<Void> purgeUnverifiedAccounts(ScheduledTasks tasks, CommandBus bus) {
        return tasks.recurring(TaskName.of("identity.purge-unverified-accounts"),
                TaskSchedule.daily(LocalTime.of(3, 0), ZoneOffset.UTC),
                () -> bus.dispatch(new PurgeUnverifiedAccounts()));
    }

    // One-time: one execution per scheduled key.
    @Bean
    OneTimeTask<ReminderData> sendVerificationReminder(ScheduledTasks tasks, CommandBus bus) {
        return tasks.oneTime(TaskName.of("identity.send-verification-reminder"), ReminderData.class,
                reminder -> bus.dispatch(new SendReminder(reminder.accountId())));
    }
}

@Configuration(proxyBeanMethods = false)
class OrganizationTasks {

    // Per-entity: one execution per branch, each on the branch's own cron and zone.
    @Bean
    EntityTask closeBusinessDay(ScheduledTasks tasks, CommandBus bus) {
        return tasks.perEntity(TaskName.of("organization.close-business-day"),
                branchId -> bus.dispatch(new CloseBusinessDay(BranchId.of(branchId))));
    }
}
```

- `TaskName`: `<module>.<kebab-case-name>` (checked when created). It keys the stored executions and tags the telemetry: never rename a deployed task.
- `TaskSchedule`: `fixedDelay(Duration)` (from the end of one run to the start of the next, so runs never overlap), `daily(LocalTime, ZoneId)`, `cron(expression, ZoneId)` (six fields, seconds first; checked at startup). Local times are always read in an explicit zone.
- Keys are natural keys: the entity id, or the entity id and the period (`branch-uuid:2026-09-18`). Scheduling a key that is already pending does nothing, so scheduling is idempotent:

```java
sendVerificationReminder.schedule(accountId.toString(), new ReminderData(accountId), clock.instant().plus(Duration.ofDays(1)));
```

- Per-entity schedules are `EntitySchedule(cron, zone)` (read in the entity's zone). `schedule` creates or replaces one (a branch moves to another zone); the pending run follows at once, no restart. An invalid cron is rejected there with `IllegalArgumentException`. `cancel(entityId)` removes it, `nextRun(entityId)` reads it:

```java
closeBusinessDay.schedule(branchId.toString(), new EntitySchedule("0 0 4 * * *", branch.zone()));
```

- Scheduling writes through a transaction-aware `DataSource`: called inside a command transaction, it commits or rolls back with it (like an outbox row).
- `TaskSchedulingException` (unchecked): the database could not be reached, or an entity's run is in progress while its schedule changes (retry later, e.g. by letting the event consumer fail).
- A recurring task that hands state to its next run uses `tasks.recurring(name, schedule, dataType, initialData, action)`; the action returns the next data, and `runNow(data)` moves the pending run to now with new data (see `OutboxRecoveryTask`).
- Task data is stored as JSON: records of ids and small values. Add fields only; a renamed class or field breaks pending runs.

## Failures and retries

- Actions throw on failure; they never catch and log. The scheduler retries with exponential backoff (`frappe.scheduling.initial-backoff` 30s, doubling, `frappe.scheduling.max-retries` 5 times). Then a recurring task continues on its schedule and a one-time task keeps retrying at the next step (16m with the defaults).
- Actions are idempotent: a run taken over from a dead instance, or retried after a failure that happened after its effect, runs again.
- Keep runs short (batches): on shutdown running executions get `db-scheduler.shutdown-max-wait` (10s, waited at most twice), then another instance takes over once the heartbeat expires: `db-scheduler.heartbeat-interval` 15s × `missed-heartbeats-limit` 6, so an execution of a dead instance resumes elsewhere after about 90s (checked every 30s, twice the heartbeat).

## Telemetry and logs (automatic)

- Every execution is the observation `scheduled.task`: span `scheduled task <name>` and timer tagged `scheduled.task.name` and `scheduled.task.outcome` (`success`, `failure`); the key is a span attribute only. The action runs inside it, so its queries and log lines join the trace. Do not add telemetry around tasks.
- Every failure is logged once: WARN while retries remain, ERROR once they are used up, with `frappe.scheduling.task_name`, `frappe.scheduling.task_instance`, `frappe.scheduling.consecutive_failures` and the cause.
- db-scheduler's own meters (`db_scheduler_*`) and the `db-scheduler` health indicator come from the starter.

## Configuration

- `db-scheduler.*` (application.properties): `table-name=platform.scheduled_tasks` (Flyway owns it, `db/migration/platform`; the library never creates tables), `delay-startup-until-context-ready=true`, `shutdown-max-wait=10s`, `heartbeat-interval=15s`, `missed-heartbeats-limit=6` (takeover after about 90s); library defaults otherwise (polling every 10s).
- `frappe.scheduling.*`: retry defaults in `SchedulingProperties` only.
- The scheduler runs on the application `Clock` (due times, heartbeats, retries).
- When the application context stops, the scheduler stops picking new executions first (`SchedulerPausing`); paused test contexts do not run tasks either.

## Testing

- Unit-test the action's collaborators as usual; a task bean needs no own unit test beyond what it delegates to. In unit tests of code that schedules, mock the kernel handle (`OneTimeTask`, `EntityTask`, `RecurringTask`).
- Integration tests that race schedulers build them like `RacingSchedulersIntegrationTests` (same table, serializer and interceptor, fast polling and heartbeat) with task names unique per test, and delete their rows afterwards. A test waiting for a short task interval sets `db-scheduler.polling-interval=100ms`.
