package com.frappe.platform.infrastructure.events;

import java.sql.ResultSet;
import java.sql.SQLException;
import java.sql.Timestamp;
import java.time.Duration;
import java.time.Instant;
import java.util.Collection;
import java.util.List;
import java.util.UUID;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.stereotype.Repository;

/**
 * Recovery queries on the outbox tables that Spring Modulith's registry does not offer: stuck detection, fair retry
 * selection and dead letters. The registry stays the writer of the normal publication lifecycle.
 */
@Repository
class OutboxRecoveryRepository {

    // A publication without outcome is judged by its latest attempt: the resubmission time if it was resubmitted,
    // otherwise its publication time. Modulith's own staleness monitor uses the publication time for every status,
    // which would re-fail an old event in the middle of its resubmission. The status guard keeps the update from
    // touching a row that completed or failed meanwhile.
    private static final String RELEASE_STUCK = """
            update platform.event_publication
               set status = 'FAILED'
             where status in ('PUBLISHED', 'PROCESSING', 'RESUBMITTED')
               and coalesce(last_resubmission_date, publication_date) < ?
            """;

    // Failed rows whose backoff elapsed (base * 2^(attempts - 1), capped; the exponent is bounded so the interval
    // cannot overflow), least recently attempted first: a row that keeps failing goes to the back of the queue and
    // waits longer each time, so it cannot starve newer failures.
    private static final String FIND_RETRYABLE = """
            select id
              from platform.event_publication
             where status = 'FAILED'
               and coalesce(last_resubmission_date, publication_date)
                   <= ?::timestamptz - make_interval(secs => least(
                          ?::float8 * power(2, least(greatest(coalesce(completion_attempts, 0) - 1, 0), 30)),
                          ?::float8))
             order by coalesce(last_resubmission_date, publication_date), id
             limit ?
            """;

    private static final String COUNT_IN_FLIGHT = """
            select count(*) from platform.event_publication where status = 'RESUBMITTED'
            """;

    // Move in one statement: a row is either still retried or a dead letter, never both or neither. The %s is one of
    // the fixed conditions below, never user input.
    private static final String DEAD_LETTER = """
            with given_up as (
                delete from platform.event_publication
                 where status = 'FAILED' and %s
                returning *)
            insert into platform.event_publication_dead_letter (id, listener_id, event_type, serialized_event,
                publication_date, completion_date, status, completion_attempts, last_resubmission_date,
                dead_lettered_at, reason)
            select id, listener_id, event_type, serialized_event, publication_date, completion_date, status,
                   completion_attempts, last_resubmission_date, ?, ?
              from given_up
            returning id, event_type, listener_id, completion_attempts, reason
            """;

    private static final String DEAD_LETTER_EXHAUSTED = DEAD_LETTER.formatted("coalesce(completion_attempts, 0) >= ?");

    private static final String DEAD_LETTER_BY_EVENT_TYPE = DEAD_LETTER.formatted("event_type = ?");

    private static final String DEAD_LETTER_BY_IDS = DEAD_LETTER.formatted("id = any(?)");

    private static final String FAILED_EVENT_TYPES = """
            select distinct event_type from platform.event_publication where status = 'FAILED'
            """;

    private static final String COUNT_DEAD_LETTERS = """
            select count(*) from platform.event_publication_dead_letter
            """;

    private final JdbcClient jdbc;

    /**
     * Creates the repository.
     *
     * @param jdbc client on the application data source
     */
    OutboxRecoveryRepository(JdbcClient jdbc) {
        this.jdbc = jdbc;
    }

    /**
     * Marks publications whose latest attempt started before the threshold and never reported an outcome as failed,
     * so they become eligible for resubmission.
     *
     * @param attemptStartedBefore attempts started before this instant are considered stuck
     * @return the number of released publications
     */
    int releaseStuckPublications(Instant attemptStartedBefore) {
        return jdbc.sql(RELEASE_STUCK)
                .param(Timestamp.from(attemptStartedBefore))
                .update();
    }

    /**
     * Selects failed publications due for another attempt, least recently attempted first.
     *
     * @param now the current instant
     * @param limit most ids to return
     * @param baseBackoff wait after the first attempt; doubles with every further attempt
     * @param maxBackoff cap of the wait
     * @return publication ids, in retry order
     */
    List<UUID> findRetryable(Instant now, int limit, Duration baseBackoff, Duration maxBackoff) {
        return jdbc.sql(FIND_RETRYABLE)
                .params(Timestamp.from(now), seconds(baseBackoff), seconds(maxBackoff), limit)
                .query(UUID.class)
                .list();
    }

    /**
     * Counts publications currently being resubmitted.
     *
     * @return publications in status {@code RESUBMITTED}
     */
    long countInFlight() {
        return jdbc.sql(COUNT_IN_FLIGHT).query(Long.class).single();
    }

    /**
     * Moves failed publications that used up their attempts to the dead-letter table.
     *
     * @param maxAttempts attempts after which a publication is given up
     * @param now when they are dead-lettered
     * @return the moved publications
     */
    List<DeadLetter> deadLetterExhausted(int maxAttempts, Instant now) {
        return deadLetter(DEAD_LETTER_EXHAUSTED, maxAttempts, DeadLetterReason.MAX_ATTEMPTS_EXHAUSTED, now);
    }

    /**
     * Moves the failed publications of one event type to the dead-letter table.
     *
     * @param eventType fully qualified class name of the event
     * @param reason why they are given up
     * @param now when they are dead-lettered
     * @return the moved publications
     */
    List<DeadLetter> deadLetterByEventType(String eventType, DeadLetterReason reason, Instant now) {
        return deadLetter(DEAD_LETTER_BY_EVENT_TYPE, eventType, reason, now);
    }

    /**
     * Moves the given publications to the dead-letter table if they are still failed.
     *
     * @param ids publication ids
     * @param reason why they are given up
     * @param now when they are dead-lettered
     * @return the moved publications
     */
    List<DeadLetter> deadLetterByIds(Collection<UUID> ids, DeadLetterReason reason, Instant now) {
        return deadLetter(DEAD_LETTER_BY_IDS, ids.toArray(UUID[]::new), reason, now);
    }

    /**
     * Lists the event types that have failed publications.
     *
     * @return fully qualified class names
     */
    List<String> failedEventTypes() {
        return jdbc.sql(FAILED_EVENT_TYPES).query(String.class).list();
    }

    private List<DeadLetter> deadLetter(String sql, Object condition, DeadLetterReason reason, Instant now) {
        return jdbc.sql(sql)
                .params(condition, Timestamp.from(now), reason.name())
                .query(OutboxRecoveryRepository::deadLetter)
                .list();
    }

    /**
     * Counts all dead letters.
     *
     * @return rows in {@code platform.event_publication_dead_letter}
     */
    long countDeadLetters() {
        return jdbc.sql(COUNT_DEAD_LETTERS).query(Long.class).single();
    }

    private static DeadLetter deadLetter(ResultSet row, int rowNumber) throws SQLException {
        return new DeadLetter(
                row.getObject("id", UUID.class),
                row.getString("event_type"),
                row.getString("listener_id"),
                row.getInt("completion_attempts"),
                DeadLetterReason.valueOf(row.getString("reason")));
    }

    private static double seconds(Duration duration) {
        return duration.toMillis() / 1000.0;
    }
}
