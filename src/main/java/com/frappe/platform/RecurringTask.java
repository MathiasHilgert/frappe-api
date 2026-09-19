package com.frappe.platform;

/**
 * A recurring task: one execution for the whole cluster, scheduled at startup and rescheduled after every run.
 *
 * @param <T> the data handed from one run to the next ({@link Void} for none)
 */
public interface RecurringTask<T> extends ScheduledTask {

    /**
     * Moves the pending run to now and wakes this instance's scheduler. The retry count of a failing task starts over.
     *
     * @param nextData the data of that run, or {@code null} to keep the stored data
     * @return {@code true} if moved; {@code false} if a run is in progress (it covers the same work), the task is not
     *     scheduled yet (startup schedules it at once) or another instance changed it at the same moment
     * @throws TaskSchedulingException if the database could not be reached
     */
    boolean runNow(T nextData);
}
