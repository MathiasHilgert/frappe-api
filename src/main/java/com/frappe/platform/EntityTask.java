package com.frappe.platform;

import java.time.Instant;
import java.util.Optional;

/**
 * A recurring task with one execution per entity, each on the entity's own {@link EntitySchedule}. Calls inside a
 * transaction commit or roll back with it.
 */
public interface EntityTask extends ScheduledTask {

    /**
     * Creates or replaces an entity's schedule; its next run follows the new schedule at once, without a restart.
     *
     * @param entityId the entity's natural key, handed to the task on every run
     * @param schedule when the entity's task runs
     * @throws IllegalArgumentException if the cron expression is invalid
     * @throws TaskSchedulingException if the entity's task is running right now (retry later) or the database could
     *     not be reached
     */
    void schedule(String entityId, EntitySchedule schedule);

    /**
     * Stops an entity's runs.
     *
     * @param entityId the entity's natural key
     * @return {@code true} if a schedule was removed, {@code false} if the entity had none
     * @throws TaskSchedulingException if the entity's task is running right now or the database could not be reached
     */
    boolean cancel(String entityId);

    /**
     * When an entity's task runs next.
     *
     * @param entityId the entity's natural key
     * @return the next run, empty if the entity has no schedule
     * @throws TaskSchedulingException if the database could not be reached
     */
    Optional<Instant> nextRun(String entityId);
}
