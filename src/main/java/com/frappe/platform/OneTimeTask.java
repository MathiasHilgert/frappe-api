package com.frappe.platform;

import java.time.Instant;

/**
 * A task run once per scheduled instance, e.g. a delayed follow-up. A run that still fails after its retries is given
 * up (removed, logged at ERROR, counted as {@code scheduled.task.exhausted}); schedule it again once the cause is
 * fixed.
 *
 * @param <T> the instance data, stored as JSON
 */
public interface OneTimeTask<T> extends ScheduledTask {

    /**
     * Schedules one run, unless one with the same key is already pending: the natural key (the entity id, or the
     * entity id and the period) makes scheduling idempotent. Called inside a transaction, it commits or rolls back with
     * it.
     *
     * @param key the natural key of the run
     * @param data the data handed to the task
     * @param at when it runs
     * @return {@code true} if scheduled, {@code false} if a run with this key was already pending
     * @throws TaskSchedulingException if the database could not be reached
     */
    boolean schedule(String key, T data, Instant at);
}
