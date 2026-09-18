package com.frappe.platform.infrastructure.events;

import java.time.Clock;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.dao.DataAccessException;
import org.springframework.modulith.events.FailedEventPublications;
import org.springframework.modulith.events.ResubmissionOptions;

/**
 * One scheduled recovery run: fails publications stuck without outcome, then resubmits a bounded batch of failed
 * publications to their listeners (for externalized events, the NATS relay).
 *
 * <p>Complements the resubmission on NATS reconnect: a publish can fail while the connection survives (a slow or
 * paused server), and a publication can be left behind by an instance that died, neither of which triggers a
 * reconnect. A publication may still be delivered twice (an attempt judged stuck that was only slow): JetStream drops
 * the duplicate within its 10-minute window by {@code Nats-Msg-Id}, later ones are dropped by the consumer inbox on
 * {@code eventId}.
 */
final class FailedPublicationResubmitter implements Runnable {

    private static final Logger log = LoggerFactory.getLogger(FailedPublicationResubmitter.class);

    private final FailedEventPublications failedPublications;
    private final OutboxRecoveryRepository outbox;
    private final OutboxRecoveryProperties properties;
    private final Clock clock;
    private final ResubmissionOptions options;

    /**
     * Creates the resubmitter.
     *
     * @param failedPublications Modulith's entry point for resubmitting failed publications
     * @param outbox recovery queries on the outbox tables
     * @param properties batch size, interval and stuck threshold
     * @param clock the application clock
     */
    FailedPublicationResubmitter(
            FailedEventPublications failedPublications,
            OutboxRecoveryRepository outbox,
            OutboxRecoveryProperties properties,
            Clock clock) {
        this.failedPublications = failedPublications;
        this.outbox = outbox;
        this.properties = properties;
        this.clock = clock;
        // maxInFlight deliberately reuses the batch size: it counts publications still RESUBMITTED from earlier
        // runs, so a slow NATS never has more than one batch outstanding.
        this.options = ResubmissionOptions.defaults()
                .withBatchSize(properties.batchSize())
                .withMaxInFlight(properties.batchSize());
    }

    /** Runs one recovery pass; a database failure is logged and the next run tries again. */
    @Override
    public void run() {
        try {
            outbox.releaseStuckPublications(clock.instant().minus(properties.stuckAfter()));
            failedPublications.resubmit(options);
        } catch (DataAccessException e) {
            // Scheduled task boundary: nobody above can handle it, and the next run retries.
            log.atWarn()
                    .addKeyValue(LogFields.RECOVERY_INTERVAL, properties.interval())
                    .addKeyValue(LogFields.BATCH_SIZE, properties.batchSize())
                    .setCause(e)
                    .log("Recovering event publications failed; retrying after the recovery interval");
        }
    }
}
