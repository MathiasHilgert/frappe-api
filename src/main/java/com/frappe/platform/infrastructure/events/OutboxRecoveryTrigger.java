package com.frappe.platform.infrastructure.events;

import com.frappe.platform.RecurringTask;
import com.frappe.platform.TaskSchedulingException;
import com.frappe.platform.infrastructure.MessagingTransportRecovered;
import com.frappe.platform.infrastructure.events.OutboxObservations.Trigger;
import java.util.concurrent.Executor;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.context.event.EventListener;

/**
 * Runs a recovery pass at once when a messaging transport came back: moves the one {@link OutboxRecoveryTask} run to
 * now with {@link Trigger#TRANSPORT_RECOVERED}, which ignores the backoff. Published by the transport adapter on its
 * setup thread, which does not know the outbox; the reschedule (database I/O) runs on the application task executor, so
 * a slow database never delays the transport setup. Moving the run also resets the task's failure count (see
 * {@link RecurringTask#runNow}).
 */
final class OutboxRecoveryTrigger {

    private static final Logger log = LoggerFactory.getLogger(OutboxRecoveryTrigger.class);

    private final RecurringTask<Trigger> recovery;
    private final Executor executor;

    /**
     * Creates the trigger.
     *
     * @param recovery the recovery task
     * @param executor runs the reschedule off the transport's thread
     */
    OutboxRecoveryTrigger(RecurringTask<Trigger> recovery, Executor executor) {
        this.recovery = recovery;
        this.executor = executor;
    }

    /**
     * Hands moving the recovery pass to now to the executor and returns at once.
     *
     * @param recovered the transport that came back
     */
    @EventListener
    void onTransportRecovered(MessagingTransportRecovered recovered) {
        executor.execute(this::runRecoveryNow);
    }

    // false: a pass is running (it covers the same rows), the task is not scheduled yet (startup runs it at once), or
    // another instance moved it at the same moment; db-scheduler logs that last race at WARN too, which is expected.
    private void runRecoveryNow() {
        try {
            if (!recovery.runNow(Trigger.TRANSPORT_RECOVERED)) {
                log.debug("Recovery pass not moved (running, not scheduled yet or changed concurrently); the running"
                        + " or scheduled pass covers the publications the recovered transport left behind");
            }
        } catch (TaskSchedulingException e) {
            // Background task boundary: nothing above handles it, and the scheduled pass recovers the same rows.
            log.atWarn()
                    .setCause(e)
                    .log("Triggering outbox recovery after the transport came back failed; the scheduled pass runs"
                            + " it instead");
        }
    }
}
