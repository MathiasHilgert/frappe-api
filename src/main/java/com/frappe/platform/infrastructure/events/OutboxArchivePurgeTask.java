package com.frappe.platform.infrastructure.events;

import com.frappe.platform.RecurringTask;
import com.frappe.platform.ScheduledTasks;
import com.frappe.platform.TaskName;
import com.frappe.platform.TaskSchedule;
import java.time.Duration;

/**
 * The archive purge as one cluster-wide scheduled task ({@code platform.purge-event-archive}): only one run at a time
 * across all instances, without an in-process lock.
 */
final class OutboxArchivePurgeTask {

    /** Task name, the key of the one stored execution. */
    static final TaskName NAME = TaskName.of("platform.purge-event-archive");

    private OutboxArchivePurgeTask() {}

    /**
     * Declares the task. Fixed delay, not rate: a run that takes a while never overlaps the next one.
     *
     * @param tasks the platform's task conventions
     * @param purger runs one purge
     * @param interval pause between the end of one run and the start of the next
     * @return the task, a bean the scheduler picks up
     */
    static RecurringTask<Void> declare(ScheduledTasks tasks, OutboxArchivePurger purger, Duration interval) {
        return tasks.recurring(NAME, TaskSchedule.fixedDelay(interval), purger::purge);
    }
}
