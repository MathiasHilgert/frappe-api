package com.frappe.platform.infrastructure.scheduling;

import com.github.kagkarlsson.scheduler.Scheduler;
import org.springframework.context.SmartLifecycle;

/**
 * Stops the scheduler picking due executions when the application context stops, and resumes it when the context
 * starts again. On shutdown this runs first (highest phase), so no new task starts while the rest of the application
 * shuts down; running executions finish when the scheduler bean is destroyed ({@code db-scheduler.shutdown-max-wait}).
 * Paused test contexts (Spring's context cache) no longer run tasks against their stale beans either. Heartbeats go on
 * while paused, so running executions are not mistaken for dead ones.
 */
final class SchedulerPausing implements SmartLifecycle {

    private final Scheduler scheduler;

    /**
     * Creates the lifecycle.
     *
     * @param scheduler the application's scheduler
     */
    SchedulerPausing(Scheduler scheduler) {
        this.scheduler = scheduler;
    }

    /** Resumes picking due executions; the scheduler itself is started by the db-scheduler starter. */
    @Override
    public void start() {
        scheduler.resume();
    }

    /** Stops picking due executions; running ones continue. */
    @Override
    public void stop() {
        scheduler.pause();
    }

    /**
     * Whether the scheduler picks due executions.
     *
     * @return {@code true} unless paused
     */
    @Override
    public boolean isRunning() {
        return !scheduler.getSchedulerState().isPaused();
    }
}
