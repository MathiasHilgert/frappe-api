package com.frappe.platform;

import com.github.kagkarlsson.scheduler.task.StateReturningExecutionHandler;
import com.github.kagkarlsson.scheduler.task.VoidExecutionHandler;
import com.github.kagkarlsson.scheduler.task.helper.OneTimeTask;
import com.github.kagkarlsson.scheduler.task.helper.RecurringTask;
import com.github.kagkarlsson.scheduler.task.helper.RecurringTaskWithPersistentSchedule;
import com.github.kagkarlsson.scheduler.task.schedule.Schedule;

/**
 * Declares cluster-safe scheduled tasks (db-scheduler) with the platform conventions; inject it where a module
 * declares its task beans, in its {@code infrastructure}. Every due execution runs on exactly one instance, and one that
 * was running on an instance that died is taken over once its heartbeat expires.
 *
 * <p>Conventions applied to every task:
 *
 * <ul>
 *   <li>Name {@code <module>.<kebab-case-name>} ({@code platform.outbox-recovery}); it is the key of the stored
 *       executions and the {@code scheduled.task.name} tag, so it never changes once deployed.
 *   <li>A failed execution is retried with exponential backoff ({@code frappe.scheduling.initial-backoff}, doubling,
 *       {@code frappe.scheduling.max-retries} times). Afterwards recurring tasks continue on their schedule and one-time
 *       tasks keep retrying at the next backoff step, so no work is lost.
 *   <li>Every execution is observed ({@code scheduled.task}) and every failure is logged; handlers add no telemetry.
 * </ul>
 *
 * <p>Handlers must be idempotent: an execution taken over from a dead instance may run a second time.
 */
public interface ScheduledTasks {

    /**
     * A task that runs on a fixed schedule, one execution for the whole cluster; scheduled at startup.
     *
     * @param name the task name, {@code <module>.<kebab-case-name>}
     * @param schedule when it runs ({@code Schedules.fixedDelay}, {@code Schedules.cron}, {@code Schedules.daily})
     * @param handler the work
     * @return the task, to be declared as a bean
     * @throws IllegalArgumentException if the name breaks the naming rule
     */
    RecurringTask<Void> recurring(String name, Schedule schedule, VoidExecutionHandler<Void> handler);

    /**
     * A recurring task that hands state from one execution to the next: the handler receives the stored data and
     * returns the data of the next execution. Anyone may replace the data of the pending execution with {@code
     * SchedulerClient.reschedule(instance, time, data)}.
     *
     * @param <T> the type of the stored data
     * @param name the task name, {@code <module>.<kebab-case-name>}
     * @param schedule when it runs
     * @param dataType the type of the stored data
     * @param initialData the data of the first execution
     * @param handler the work, returning the data of the next execution
     * @return the task, to be declared as a bean
     * @throws IllegalArgumentException if the name breaks the naming rule
     */
    <T> RecurringTask<T> recurring(
            String name,
            Schedule schedule,
            Class<T> dataType,
            T initialData,
            StateReturningExecutionHandler<T> handler);

    /**
     * A task run once per scheduled instance, e.g. a delayed follow-up. Schedule instances with {@code
     * SchedulerClient.scheduleIfNotExists(task.instance(naturalKey, data), time)}: the natural key (the entity id, or
     * the entity id and the period) makes scheduling idempotent.
     *
     * @param <T> the type of the instance data
     * @param name the task name, {@code <module>.<kebab-case-name>}
     * @param dataType the type of the instance data, stored as JSON
     * @param handler the work
     * @return the task, to be declared as a bean
     * @throws IllegalArgumentException if the name breaks the naming rule
     */
    <T> OneTimeTask<T> oneTime(String name, Class<T> dataType, VoidExecutionHandler<T> handler);

    /**
     * A recurring task with one execution per entity, each on the entity's own {@link EntitySchedule} (cron in the
     * entity's zone). The instance id is the entity's natural key. Create or change an entity's schedule with {@code
     * SchedulerClient.schedule(task.schedulableInstance(entityId, schedule), ScheduleOptions.WHEN_EXISTS_RESCHEDULE)};
     * the next execution follows it without a restart. Remove it with {@code SchedulerClient.cancel}.
     *
     * @param name the task name, {@code <module>.<kebab-case-name>}
     * @param handler the work for one entity; {@code instance.getId()} is the entity key
     * @return the task, to be declared as a bean
     * @throws IllegalArgumentException if the name breaks the naming rule
     */
    RecurringTaskWithPersistentSchedule<EntitySchedule> perEntity(
            String name, VoidExecutionHandler<EntitySchedule> handler);
}
