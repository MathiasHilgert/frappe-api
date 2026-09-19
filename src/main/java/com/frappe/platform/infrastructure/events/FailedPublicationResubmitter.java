package com.frappe.platform.infrastructure.events;

import java.time.Clock;
import java.time.Instant;
import java.util.HashSet;
import java.util.Set;
import java.util.UUID;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.dao.DataAccessException;
import org.springframework.modulith.events.EventPublication;
import org.springframework.modulith.events.IncompleteEventPublications;
import org.springframework.util.ClassUtils;
import tools.jackson.core.JacksonException;

/**
 * One scheduled recovery run over the outbox:
 *
 * <ol>
 *   <li>fails attempts stuck without outcome (judged by their latest attempt),
 *   <li>moves publications that used up {@code max-attempts}, or whose event type is gone from the classpath, to the
 *       dead-letter table, logging each once,
 *   <li>resubmits failed publications whose backoff elapsed, least recently attempted first, keeping at most {@code
 *       batch-size} in flight; a selected publication whose payload no longer deserializes is dead-lettered instead,
 *       without affecting the rest of the batch,
 *   <li>refreshes the dead-letter gauge.
 * </ol>
 *
 * <p>Complements the resubmission on NATS reconnect: a publish can fail while the connection survives (a slow or
 * paused server), and a publication can be left behind by an instance that died, neither of which triggers a
 * reconnect. A publication may still be delivered twice (an attempt judged stuck that was only slow): JetStream drops
 * the duplicate within its 10-minute window by {@code Nats-Msg-Id}, later ones are dropped by the consumer inbox on
 * {@code eventId}.
 */
final class FailedPublicationResubmitter implements Runnable {

    private static final Logger log = LoggerFactory.getLogger(FailedPublicationResubmitter.class);

    private final IncompleteEventPublications incompletePublications;
    private final OutboxRecoveryRepository outbox;
    private final DeadLetterMetrics metrics;
    private final OutboxRecoveryProperties properties;
    private final Clock clock;

    /**
     * Creates the resubmitter.
     *
     * @param incompletePublications Modulith's entry point for resubmitting publications
     * @param outbox recovery queries on the outbox tables
     * @param metrics the dead-letter gauge
     * @param properties recovery settings
     * @param clock the application clock
     */
    FailedPublicationResubmitter(
            IncompleteEventPublications incompletePublications,
            OutboxRecoveryRepository outbox,
            DeadLetterMetrics metrics,
            OutboxRecoveryProperties properties,
            Clock clock) {
        this.incompletePublications = incompletePublications;
        this.outbox = outbox;
        this.metrics = metrics;
        this.properties = properties;
        this.clock = clock;
    }

    /** Runs one recovery pass; a database failure is logged and the next run tries again. */
    @Override
    public void run() {
        try {
            var now = clock.instant();
            outbox.releaseStuckPublications(now.minus(properties.stuckAfter()));
            outbox.deadLetterExhausted(properties.maxAttempts(), now).forEach(this::logDeadLetter);
            deadLetterUnknownEventTypes(now);
            resubmitDueFailures(now);
            metrics.recordDeadLetters(outbox.countDeadLetters());
        } catch (DataAccessException e) {
            // Scheduled task boundary: nobody above can handle it, and the next run retries.
            log.atWarn()
                    .addKeyValue(LogFields.RECOVERY_INTERVAL, properties.interval())
                    .addKeyValue(LogFields.BATCH_SIZE, properties.batchSize())
                    .setCause(e)
                    .log("Recovering event publications failed; retrying after the recovery interval");
        }
    }

    // The registry silently skips rows whose class cannot be loaded, so they would never be attempted, never reach
    // max-attempts and keep occupying the retry selection.
    private void deadLetterUnknownEventTypes(Instant now) {
        outbox.failedEventTypes().stream()
                .filter(eventType -> !ClassUtils.isPresent(eventType, getClass().getClassLoader()))
                .flatMap(eventType ->
                        outbox.deadLetterByEventType(eventType, DeadLetterReason.UNKNOWN_EVENT_TYPE, now).stream())
                .forEach(this::logDeadLetter);
    }

    private void resubmitDueFailures(Instant now) {
        // The batch size is deliberately also the in-flight limit: publications still RESUBMITTED from earlier runs
        // use up the batch, so a slow NATS never has more than one batch outstanding.
        var headroom = properties.batchSize() - outbox.countInFlight();
        if (headroom <= 0) {
            return;
        }
        var due = outbox.findRetryable(clock.instant(), (int) headroom, properties.interval(), properties.maxBackoff());
        if (due.isEmpty()) {
            return;
        }
        // The selection happens in SQL (backoff, fairness, limit); Modulith's own failed-publication query orders by
        // publication date and limits before filtering, which would let old failures starve newer ones.
        var selected = new HashSet<>(due);
        var unreadable = new HashSet<UUID>();
        incompletePublications.resubmitIncompletePublications(
                publication -> selected.contains(publication.getIdentifier()) && isReadable(publication, unreadable));
        if (!unreadable.isEmpty()) {
            outbox.deadLetterByIds(unreadable, DeadLetterReason.UNREADABLE_PAYLOAD, now)
                    .forEach(this::logDeadLetter);
        }
    }

    // Deserializes before Modulith marks the row RESUBMITTED: failing here keeps the row FAILED and out of this
    // batch, instead of leaving it stuck in flight until stuck-after. The registry caches the deserialized event.
    private static boolean isReadable(EventPublication publication, Set<UUID> unreadable) {
        try {
            publication.getEvent();
            return true;
        } catch (JacksonException e) {
            unreadable.add(publication.getIdentifier());
            return false;
        }
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
