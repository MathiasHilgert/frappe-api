package com.frappe.platform.infrastructure.events;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.dao.DataAccessException;
import org.springframework.modulith.events.FailedEventPublications;
import org.springframework.modulith.events.ResubmissionOptions;

/**
 * One scheduled recovery run: resubmits a bounded batch of failed publications to their listeners (for externalized
 * events, the NATS relay). Stale publications reach it too, because Modulith's staleness monitor marks them failed.
 *
 * <p>Complements the resubmission on NATS reconnect: a publish can fail while the connection survives (a slow or
 * paused server), and a publication can be left behind by an instance that died, neither of which triggers a
 * reconnect. Duplicate deliveries are harmless: JetStream deduplicates by event id.
 */
final class FailedPublicationResubmitter implements Runnable {

    private static final Logger log = LoggerFactory.getLogger(FailedPublicationResubmitter.class);

    private final FailedEventPublications failedPublications;
    private final OutboxRecoveryProperties properties;
    private final ResubmissionOptions options;

    /**
     * Creates the resubmitter.
     *
     * @param failedPublications Modulith's entry point for resubmitting failed publications
     * @param properties batch size and interval
     */
    FailedPublicationResubmitter(FailedEventPublications failedPublications, OutboxRecoveryProperties properties) {
        this.failedPublications = failedPublications;
        this.properties = properties;
        // maxInFlight counts publications still RESUBMITTED from earlier runs, so a slow NATS is not flooded.
        this.options = ResubmissionOptions.defaults()
                .withBatchSize(properties.batchSize())
                .withMaxInFlight(properties.batchSize());
    }

    /** Resubmits one batch; a database failure is logged and the next run tries again. */
    @Override
    public void run() {
        try {
            failedPublications.resubmit(options);
        } catch (DataAccessException e) {
            // Scheduled task boundary: nobody above can handle it, and the next run retries.
            log.atWarn()
                    .addKeyValue(LogFields.RECOVERY_INTERVAL, properties.interval())
                    .setCause(e)
                    .log("Resubmitting failed event publications failed; retrying after the recovery interval");
        }
    }
}
