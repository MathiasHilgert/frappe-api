package com.frappe.platform.infrastructure.events;

import com.frappe.platform.infrastructure.MessagingTransportRecovered;
import com.frappe.platform.infrastructure.events.OutboxObservations.Trigger;
import com.github.kagkarlsson.scheduler.Scheduler;
import com.github.kagkarlsson.scheduler.exceptions.TaskInstanceCurrentlyExecutingException;
import com.github.kagkarlsson.scheduler.exceptions.TaskInstanceNotFoundException;
import com.github.kagkarlsson.scheduler.task.TaskInstanceId;
import com.github.kagkarlsson.scheduler.task.helper.RecurringTask;
import com.github.kagkarlsson.shaded.jdbc.SQLRuntimeException;
import java.time.Clock;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.context.event.EventListener;

/**
 * Runs a recovery pass at once when a messaging transport came back: moves the one {@link OutboxRecoveryTask}
 * execution to now with {@link Trigger#TRANSPORT_RECOVERED} and wakes this instance's poller. Published by the transport
 * adapter (on its setup thread), which does not know the outbox; the reschedule is a single update, so that thread does
 * no recovery work.
 */
final class OutboxRecoveryTrigger {

    private static final Logger log = LoggerFactory.getLogger(OutboxRecoveryTrigger.class);

    private static final TaskInstanceId RECOVERY = TaskInstanceId.of(OutboxRecoveryTask.NAME, RecurringTask.INSTANCE);

    private final Scheduler scheduler;
    private final Clock clock;

    /**
     * Creates the trigger.
     *
     * @param scheduler the application's scheduler
     * @param clock the application clock
     */
    OutboxRecoveryTrigger(Scheduler scheduler, Clock clock) {
        this.scheduler = scheduler;
        this.clock = clock;
    }

    /**
     * Moves the recovery pass to now.
     *
     * @param recovered the transport that came back
     */
    @EventListener
    void onTransportRecovered(MessagingTransportRecovered recovered) {
        try {
            scheduler.reschedule(RECOVERY, clock.instant(), Trigger.TRANSPORT_RECOVERED);
            scheduler.triggerCheckForDueExecutions();
        } catch (TaskInstanceCurrentlyExecutingException e) {
            log.debug("A recovery pass is running; it covers the publications the recovered transport left behind");
        } catch (TaskInstanceNotFoundException e) {
            log.debug("Outbox recovery not scheduled yet; the scheduler runs its first pass when it starts");
        } catch (SQLRuntimeException e) {
            // Transport setup boundary: nothing above handles it, and the scheduled pass recovers the same rows.
            log.atWarn()
                    .setCause(e)
                    .log("Triggering outbox recovery after the transport came back failed; the scheduled pass runs"
                            + " it instead");
        }
    }
}
