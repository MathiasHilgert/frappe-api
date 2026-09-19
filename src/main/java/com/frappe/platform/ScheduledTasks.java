package com.frappe.platform;

import java.util.function.Consumer;
import java.util.function.UnaryOperator;

/**
 * Declares cluster-safe scheduled tasks with the platform conventions; inject it where a module declares its task
 * beans, in its {@code infrastructure}, and declare the returned task as a bean. Every due execution runs on exactly
 * one instance, and one that was running on an instance that died is taken over once its heartbeat expires.
 *
 * <p>Conventions applied to every task:
 *
 * <ul>
 *   <li>A failed run is retried with exponential backoff ({@code frappe.scheduling.initial-backoff}, doubling,
 *       {@code frappe.scheduling.max-retries} times). Afterwards recurring tasks continue on their schedule and
 *       one-time tasks keep retrying at the next backoff step.
 *   <li>Every run is observed ({@code scheduled.task}) and every failure is logged; actions add no telemetry.
 *   <li>Actions throw on failure; they never catch to log.
 * </ul>
 *
 * <p>Actions must be idempotent: a run taken over from a dead instance, or retried after a late failure, runs again.
 */
public interface ScheduledTasks {

    /**
     * A task that runs on a schedule, one execution for the whole cluster.
     *
     * @param name the task name
     * @param schedule when it runs
     * @param action the work
     * @return the task, to be declared as a bean
     */
    RecurringTask<Void> recurring(TaskName name, TaskSchedule schedule, Runnable action);

    /**
     * A recurring task that hands data from one run to the next: the action receives the stored data and returns the
     * data of the next run. {@link RecurringTask#runNow} can replace the data of the pending run.
     *
     * @param <T> the type of the stored data, stored as JSON
     * @param name the task name
     * @param schedule when it runs
     * @param dataType the type of the stored data
     * @param initialData the data of the first run
     * @param action the work, returning the data of the next run
     * @return the task, to be declared as a bean
     */
    <T> RecurringTask<T> recurring(
            TaskName name, TaskSchedule schedule, Class<T> dataType, T initialData, UnaryOperator<T> action);

    /**
     * A task run once per scheduled instance ({@link OneTimeTask#schedule}).
     *
     * @param <T> the type of the instance data, stored as JSON: a record of ids and small values
     * @param name the task name
     * @param dataType the type of the instance data
     * @param action the work, receiving the instance data
     * @return the task, to be declared as a bean
     */
    <T> OneTimeTask<T> oneTime(TaskName name, Class<T> dataType, Consumer<T> action);

    /**
     * A recurring task with one execution per entity, each on the entity's own {@link EntitySchedule} ({@link
     * EntityTask#schedule}).
     *
     * @param name the task name
     * @param action the work for one entity, receiving the entity's natural key
     * @return the task, to be declared as a bean
     */
    EntityTask perEntity(TaskName name, Consumer<String> action);
}
