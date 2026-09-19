package com.frappe.platform.infrastructure.events;

import com.frappe.platform.RecurringTask;
import com.frappe.platform.ScheduledTasks;
import com.frappe.platform.TaskName;
import com.frappe.platform.TaskSchedule;
import com.frappe.platform.infrastructure.events.OutboxObservations.Trigger;
import java.time.Duration;

/**
 * The outbox recovery as one cluster-wide scheduled task ({@code platform.outbox-recovery}): only one pass runs at a
 * time across all instances, without an in-process lock. The stored data is the {@link Trigger} of the next pass:
 * {@link Trigger#SCHEDULED} after every pass, {@link Trigger#TRANSPORT_RECOVERED} when {@link OutboxRecoveryTrigger}
 * moved the pass to now.
 */
final class OutboxRecoveryTask {

    /** Task name, the key of the one stored execution. */
    static final TaskName NAME = TaskName.of("platform.outbox-recovery");

    private OutboxRecoveryTask() {}

    /**
     * Declares the task. Fixed delay, not rate: a pass that waits on a slow NATS never overlaps the next one.
     *
     * @param tasks the platform's task conventions
     * @param resubmitter runs one pass
     * @param interval pause between the end of one pass and the start of the next
     * @return the task, a bean the scheduler picks up
     */
    static RecurringTask<Trigger> declare(
            ScheduledTasks tasks, FailedPublicationResubmitter resubmitter, Duration interval) {
        return tasks.recurring(NAME, TaskSchedule.fixedDelay(interval), Trigger.class, Trigger.SCHEDULED, trigger -> {
            resubmitter.recover(trigger);
            return Trigger.SCHEDULED;
        });
    }
}
