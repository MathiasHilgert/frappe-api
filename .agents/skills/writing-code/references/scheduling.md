# Scheduled tasks

Time-based work (purges, a branch's business-day close, reports, outbox recovery) runs on db-scheduler 16.12.0 inside the app, on our Postgres: every due execution runs on exactly one instance, and one that was running on an instance that died is taken over once its heartbeat expires. Reactive work stays on domain events and the outbox. Never use `@Scheduled`, `@EnableScheduling`, a `TaskScheduler`, an in-process lock or Spring Modulith Moments (`spring.modulith.moments.enabled=false`): each of them runs once per instance.

## Declaring a task

A task is a bean in the module's `infrastructure`, built with the kernel's `com.frappe.platform.ScheduledTasks`; the db-scheduler starter collects every `Task` bean. The handler calls the module's bus or a port, like a controller does; it holds no business logic.

```java
@Configuration(proxyBeanMethods = false)
class IdentityTasks {

    // Fixed recurring: one execution for the whole cluster, scheduled at startup.
    @Bean
    RecurringTask<Void> purgeUnverifiedAccounts(ScheduledTasks tasks, CommandBus bus) {
        return tasks.recurring("identity.purge-unverified-accounts", Schedules.cron("0 0 3 * * *", ZoneOffset.UTC),
                (instance, context) -> bus.dispatch(new PurgeUnverifiedAccounts()));
    }

    // One-time: one execution per scheduled instance.
    @Bean
    OneTimeTask<ReminderData> sendVerificationReminder(ScheduledTasks tasks, CommandBus bus) {
        return tasks.oneTime("identity.send-verification-reminder", ReminderData.class,
                (instance, context) -> bus.dispatch(new SendReminder(instance.getData().accountId())));
    }
}

@Configuration(proxyBeanMethods = false)
class OrganizationTasks {

    // Per-entity: one execution per branch, each on the branch's own cron and zone.
    @Bean
    RecurringTaskWithPersistentSchedule<EntitySchedule> closeBusinessDay(ScheduledTasks tasks, CommandBus bus) {
        return tasks.perEntity("organization.close-business-day",
                (instance, context) -> bus.dispatch(new CloseBusinessDay(BranchId.of(instance.getId()))));
    }
}
```

- Name: `<module>.<kebab-case-name>` (checked at startup). It keys the stored executions and tags the telemetry: never rename a deployed task.
- Instance ids are natural keys: the entity id, or the entity id and the period (`branch-uuid:2026-09-18`). Scheduling an existing key does nothing, so scheduling is idempotent:

```java
scheduler.scheduleIfNotExists(sendVerificationReminder.instance(accountId.toString(), new ReminderData(accountId)),
        clock.instant().plus(Duration.ofDays(1)));
```

- Per-entity schedules are `EntitySchedule(cron, zone)` (six-field cron, seconds first, read in the entity's zone). Create it or change it (a branch moves to another zone) with an upsert; the pending execution follows at once, no restart. Look up or cancel by `TaskInstanceId.of(name, entityId)`:

```java
scheduler.schedule(closeBusinessDay.schedulableInstance(branchId.toString(), new EntitySchedule("0 0 4 * * *", zone)),
        ScheduleOptions.WHEN_EXISTS_RESCHEDULE);
```

- Inject `SchedulerClient` to schedule. It writes through a transaction-aware `DataSource`: called inside a command transaction, the execution commits or rolls back with it (like an outbox row).
- A recurring task that hands state to its next run uses `tasks.recurring(name, schedule, dataType, initialData, handler)`; the handler returns the next data (see `OutboxRecoveryTask`).
- Task data is stored as JSON (`Jackson3Serializer`): records of ids and small values. Add fields only; a renamed class or field breaks pending executions.

## Failures and retries

- Handlers throw on failure; they never catch and log. The scheduler retries with exponential backoff (`frappe.scheduling.initial-backoff` 30s, doubling, `frappe.scheduling.max-retries` 5 times). Then a recurring task continues on its schedule and a one-time task keeps retrying at the next step (16m with the defaults) until it succeeds: no work is lost.
- Handlers are idempotent: an execution taken over from a dead instance, or retried after a failure that happened after its effect, runs again.
- Long handlers check `context.getSchedulerState().isShuttingDown()` and stop early: on shutdown running executions get `db-scheduler.shutdown-max-wait` (10s, waited at most twice), then another instance takes over after the heartbeat expires (`db-scheduler.heartbeat-interval` 5m × `missed-heartbeats-limit` 6).

## Telemetry and logs (automatic)

- Every execution is the observation `scheduled.task`: span `scheduled task <name>` and timer tagged `scheduled.task.name` and `scheduled.task.outcome` (`success`, `failure`); the instance id is a span attribute only. The handler runs inside it, so its queries and log lines join the trace. Do not add telemetry around tasks.
- Every failure is logged once: WARN while retries remain, ERROR once they are used up, with `frappe.scheduling.task_name`, `frappe.scheduling.task_instance`, `frappe.scheduling.consecutive_failures` and the cause.
- db-scheduler's own meters (`db_scheduler_*`) and the `db-scheduler` health indicator come from the starter.

## Configuration

- `db-scheduler.*` (application.properties): `table-name=platform.scheduled_tasks` (Flyway owns it, `db/migration/platform`; the library never creates tables), `delay-startup-until-context-ready=true`, `shutdown-max-wait=10s`; library defaults otherwise (polling every 10s).
- `frappe.scheduling.*`: retry defaults in `SchedulingProperties` only.
- When the application context stops, the scheduler stops picking new executions first (`SchedulerPausing`); paused test contexts do not run tasks either.

## Testing

- Unit-test the handler's collaborators as usual; a task bean needs no own unit test beyond what it delegates to.
- Integration tests that race schedulers build them like `RacingSchedulersIntegrationTests` (same table, serializer, interceptor and listener, fast polling and heartbeat) with task names unique per test, and delete their rows afterwards. A test that waits for a short task interval sets `db-scheduler.polling-interval=100ms`.
