package com.frappe.platform.infrastructure.events;

import com.frappe.platform.infrastructure.MessagingTransportRecovered;
import com.frappe.platform.infrastructure.events.OutboxObservations.Trigger;
import com.frappe.platform.infrastructure.events.PublicationRedelivery.Outcome;
import io.micrometer.observation.Observation;
import io.micrometer.observation.ObservationRegistry;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.util.EnumMap;
import java.util.HashSet;
import java.util.Set;
import java.util.UUID;
import java.util.concurrent.Executor;
import java.util.concurrent.locks.ReentrantLock;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.dao.DataAccessException;

/**
 * One scheduled recovery run over the outbox:
 *
 * <ol>
 *   <li>fails attempts stuck without outcome (judged by their latest attempt),
 *   <li>moves publications that used up {@code max-attempts} to the dead-letter table, logging each once,
 *   <li>loads at most one batch of failed publications whose backoff elapsed, least recently attempted first (minus
 *       those still in flight), and resubmits each through {@link PublicationRedelivery}; a publication that can
 *       never be delivered (unknown event type or listener, unreadable payload) is dead-lettered instead, without
 *       affecting the rest of the batch,
 *   <li>refreshes the dead-letter gauge.
 * </ol>
 *
 * <p>Runs on a fixed delay and, handed to the task scheduler, at once on {@link MessagingTransportRecovered}; passes
 * never overlap. The schedule covers what a reconnect does not: a publish can fail while the connection survives (a
 * slow or paused server), and a publication can be left behind by an instance that died, neither of which triggers a
 * reconnect. Passes on several instances may select the same row; the guarded claim lets only one resubmit it, and the
 * short window in which a slow attempt is judged stuck and retried by another instance is harmless: JetStream drops the
 * duplicate within its 10-minute window by {@code Nats-Msg-Id}, later ones are dropped by the consumer inbox on {@code
 * eventId}.
 */
final class FailedPublicationResubmitter implements Runnable {

    private static final Logger log = LoggerFactory.getLogger(FailedPublicationResubmitter.class);

    private final PublicationRedelivery redelivery;
    private final OutboxRecoveryRepository outbox;
    private final DeadLetterMetrics metrics;
    private final OutboxRecoveryProperties properties;
    private final Clock clock;
    private final Executor triggerExecutor;
    private final ObservationRegistry observations;
    private final ReentrantLock runLock = new ReentrantLock();

    /**
     * Creates the resubmitter.
     *
     * @param redelivery resubmits one loaded publication
     * @param outbox recovery queries on the outbox tables
     * @param metrics the dead-letter gauge
     * @param properties recovery settings
     * @param clock the application clock
     * @param triggerExecutor runs passes triggered by a recovered transport, off the publishing thread
     * @param observations records every pass and redelivery ({@link OutboxObservations})
     */
    FailedPublicationResubmitter(
            PublicationRedelivery redelivery,
            OutboxRecoveryRepository outbox,
            DeadLetterMetrics metrics,
            OutboxRecoveryProperties properties,
            Clock clock,
            Executor triggerExecutor,
            ObservationRegistry observations) {
        this.redelivery = redelivery;
        this.outbox = outbox;
        this.metrics = metrics;
        this.properties = properties;
        this.clock = clock;
        this.triggerExecutor = triggerExecutor;
        this.observations = observations;
    }

    /** Runs one scheduled recovery pass; a database failure is logged and the next run tries again. */
    @Override
    public void run() {
        runLock.lock();
        try {
            observedPass(Trigger.SCHEDULED, properties.interval());
        } finally {
            runLock.unlock();
        }
    }

    /**
     * Hands a recovery pass to the executor when a transport came back and returns at once, so the publishing thread
     * (NATS connection setup) never does recovery work. The pass ignores the backoff, because the pending failures
     * were most likely caused by the outage; the batch still bounds it. It is dropped if a pass is already running:
     * that pass covers the same rows.
     *
     * @param recovered the transport that came back
     */
    void onTransportRecovered(MessagingTransportRecovered recovered) {
        triggerExecutor.execute(() -> {
            if (!runLock.tryLock()) {
                return;
            }
            try {
                observedPass(Trigger.TRANSPORT_RECOVERED, Duration.ZERO);
            } finally {
                runLock.unlock();
            }
        });
    }

    // One pass at a time per instance (runLock): the scheduled pass waits for a triggered one instead of overlapping
    // it, so the in-flight headroom it computes is never stale.
    private void observedPass(Trigger trigger, Duration baseBackoff) {
        var observation = OutboxObservations.recovery(observations, trigger).start();
        try (var scope = observation.openScope()) {
            recoverExclusively(baseBackoff, observation);
        } finally {
            observation.stop();
        }
    }

    private void recoverExclusively(Duration baseBackoff, Observation pass) {
        try {
            var now = clock.instant();
            outbox.releaseStuckPublications(now.minus(properties.stuckAfter()));
            outbox.deadLetterExhausted(properties.maxAttempts(), now).forEach(this::logDeadLetter);
            resubmitDueFailures(now, baseBackoff);
            metrics.recordDeadLetters(outbox.countDeadLetters());
        } catch (DataAccessException e) {
            pass.error(e);
            // Scheduled task boundary: nobody above can handle it, and the next run retries.
            log.atWarn()
                    .addKeyValue(LogFields.RECOVERY_INTERVAL, properties.interval())
                    .addKeyValue(LogFields.BATCH_SIZE, properties.batchSize())
                    .setCause(e)
                    .log("Recovering event publications failed; retrying after the recovery interval");
        }
    }

    private void resubmitDueFailures(Instant now, Duration baseBackoff) {
        // The batch size is deliberately also the in-flight limit: publications still RESUBMITTED from earlier runs
        // use up the batch, so a slow NATS never has more than one batch outstanding.
        var headroom = properties.batchSize() - outbox.countInFlight();
        if (headroom <= 0) {
            return;
        }
        // Only this batch is read, payload included (backoff, fairness and limit in SQL); rows that can never be
        // delivered are dead-lettered instead of occupying the next selections.
        var undeliverable = new EnumMap<DeadLetterReason, Set<UUID>>(DeadLetterReason.class);
        for (var publication : outbox.findRetryable(now, (int) headroom, baseBackoff, properties.maxBackoff())) {
            var reason = deadLetterReason(observedRedelivery(publication, now));
            if (reason != null) {
                undeliverable.computeIfAbsent(reason, key -> new HashSet<>()).add(publication.id());
            }
        }
        undeliverable.forEach(
                (reason, ids) -> outbox.deadLetterByIds(ids, reason, now).forEach(this::logDeadLetter));
    }

    // Tagged "error" up front and overwritten on success: Observation.observe records a thrown exception on the
    // observation and rethrows it, so a failing redelivery (a database fault, handled by the pass) is still timed
    // and tagged without a broad catch here.
    private Outcome observedRedelivery(FailedPublication publication, Instant now) {
        var observation = OutboxObservations.redelivery(observations, publication)
                .lowCardinalityKeyValue(OutboxObservations.OUTCOME, OutboxObservations.ERROR_OUTCOME);
        return observation.observe(() -> {
            var outcome = redelivery.redeliver(publication, now);
            observation.lowCardinalityKeyValue(OutboxObservations.OUTCOME, OutboxObservations.outcomeTag(outcome));
            return outcome;
        });
    }

    private static DeadLetterReason deadLetterReason(Outcome outcome) {
        return switch (outcome) {
            case UNKNOWN_EVENT_TYPE -> DeadLetterReason.UNKNOWN_EVENT_TYPE;
            case UNREADABLE_PAYLOAD -> DeadLetterReason.UNREADABLE_PAYLOAD;
            case UNKNOWN_LISTENER -> DeadLetterReason.UNKNOWN_LISTENER;
            case RESUBMITTED, CLAIMED_ELSEWHERE, LISTENER_FAILED -> null;
        };
    }

    private void logDeadLetter(DeadLetter letter) {
        log.atError()
                .addKeyValue(LogFields.PUBLICATION_ID, letter.publicationId())
                .addKeyValue(LogFields.EVENT_TYPE, letter.eventType())
                .addKeyValue(LogFields.LISTENER_ID, letter.listenerId())
                .addKeyValue(LogFields.COMPLETION_ATTEMPTS, letter.completionAttempts())
                .addKeyValue(LogFields.DEAD_LETTER_REASON, letter.reason())
                .log("Event publication moved to platform.event_publication_dead_letter; replay it manually once the"
                        + " cause is fixed");
    }
}
