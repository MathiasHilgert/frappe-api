package com.frappe.platform.infrastructure.events;

import com.frappe.platform.RecurringTask;
import com.frappe.platform.TaskSchedulingException;
import com.frappe.platform.infrastructure.MessagingTransportRecovered;
import com.frappe.platform.infrastructure.events.OutboxObservations.Trigger;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.context.event.EventListener;

/**
 * Runs a recovery pass at once when a messaging transport came back: moves the one {@link OutboxRecoveryTask} run to
 * now with {@link Trigger#TRANSPORT_RECOVERED}, which ignores the backoff. Published by the transport adapter, which
 * does not know the outbox.
 */
final class OutboxRecoveryTrigger {

    private static final Logger log = LoggerFactory.getLogger(OutboxRecoveryTrigger.class);

    private final RecurringTask<Trigger> recovery;

    /**
     * Creates the trigger.
     *
     * @param recovery the recovery task
     */
    OutboxRecoveryTrigger(RecurringTask<Trigger> recovery) {
        this.recovery = recovery;
    }

    /**
     * Moves the recovery pass to now.
     *
     * @param recovered the transport that came back
     */
    @EventListener
    void onTransportRecovered(MessagingTransportRecovered recovered) {
        try {
            if (!recovery.runNow(Trigger.TRANSPORT_RECOVERED)) {
                log.debug("Recovery pass not moved (running, not scheduled yet or changed concurrently); the running"
                        + " or scheduled pass covers the publications the recovered transport left behind");
            }
        } catch (TaskSchedulingException e) {
            // Transport setup boundary: nothing above handles it, and the scheduled pass recovers the same rows.
            log.atWarn()
                    .setCause(e)
                    .log("Triggering outbox recovery after the transport came back failed; the scheduled pass runs"
                            + " it instead");
        }
    }
}
