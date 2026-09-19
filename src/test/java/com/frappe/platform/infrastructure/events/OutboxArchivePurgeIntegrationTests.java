package com.frappe.platform.infrastructure.events;

import static org.assertj.core.api.Assertions.assertThat;

import com.frappe.TestcontainersConfiguration;
import java.sql.Timestamp;
import java.time.Duration;
import java.time.Instant;
import java.util.List;
import java.util.UUID;
import java.util.stream.IntStream;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.annotation.Import;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.test.context.ActiveProfiles;

/** Acceptance tests of FAPI-9 against real Postgres: the purge deletes only what the retention rule allows. */
@SpringBootTest(properties = {"frappe.outbox.archive.purge-batch-size=2"})
@Import(TestcontainersConfiguration.class)
@ActiveProfiles("local")
class OutboxArchivePurgeIntegrationTests {

    @Autowired
    OutboxArchivePurger purger;

    @Autowired
    JdbcTemplate jdbc;

    @AfterEach
    void cleanUp() {
        jdbc.update("delete from platform.event_publication_archive");
        jdbc.update("delete from platform.event_publication");
        jdbc.update("delete from platform.event_publication_dead_letter");
        jdbc.update("delete from platform.event_trace_context");
    }

    static final Instant NOW = Instant.now();

    @Test
    void archivedPublicationOlderThanTheRetentionIsDeletedWithItsTraceContext() {
        // Given
        var event = insertArchived(NOW.minus(Duration.ofDays(31)));
        insertTraceContext(event);

        // When
        purger.purge();

        // Then
        assertThat(archiveCount(event)).isZero();
        assertThat(traceContextCount(event)).isZero();
    }

    @Test
    void archivedPublicationWithinTheRetentionIsKept() {
        // Given
        var event = insertArchived(NOW.minus(Duration.ofDays(29)));
        insertTraceContext(event);

        // When
        purger.purge();

        // Then
        assertThat(archiveCount(event)).isOne();
        assertThat(traceContextCount(event)).isOne();
    }

    @Test
    void traceContextOfAPendingPublicationIsKept() {
        // Given
        var event = insertPending();
        insertTraceContext(event);

        // When
        purger.purge();

        // Then
        assertThat(traceContextCount(event)).isOne();
    }

    @Test
    void traceContextOfADeadLetteredPublicationIsKept() {
        // Given
        var event = insertDeadLetter();
        insertTraceContext(event);

        // When
        purger.purge();

        // Then
        assertThat(traceContextCount(event)).isOne();
    }

    @Test
    void moreArchivedRowsThanTheBatchSizeAreAllDeletedAcrossBatches() {
        // Given: purge-batch-size is 2, so five expired rows need three batches.
        List<UUID> events = IntStream.range(0, 5)
                .mapToObj(i -> insertArchived(NOW.minus(Duration.ofDays(60))))
                .toList();

        // When
        purger.purge();

        // Then
        events.forEach(event -> assertThat(archiveCount(event)).isZero());
    }

    private UUID insertArchived(Instant completionDate) {
        var id = UUID.randomUUID();
        jdbc.update(
                """
                insert into platform.event_publication_archive (id, listener_id, event_type, serialized_event,
                    publication_date, completion_date, status, completion_attempts)
                values (?, 'nats.listener', 'com.frappe.Probe', ?, ?, ?, 'COMPLETED', 1)
                """,
                id,
                "{\"eventId\":\"" + id + "\"}",
                Timestamp.from(completionDate),
                Timestamp.from(completionDate));
        return id;
    }

    private UUID insertPending() {
        var id = UUID.randomUUID();
        jdbc.update("""
                insert into platform.event_publication (id, listener_id, event_type, serialized_event,
                    publication_date, status, completion_attempts)
                values (?, 'nats.listener', 'com.frappe.Probe', ?, ?, 'FAILED', 1)
                """, id, "{\"eventId\":\"" + id + "\"}", Timestamp.from(Instant.parse("2026-08-01T12:00:00Z")));
        return id;
    }

    private UUID insertDeadLetter() {
        var id = UUID.randomUUID();
        jdbc.update(
                """
                insert into platform.event_publication_dead_letter (id, listener_id, event_type, serialized_event,
                    publication_date, status, completion_attempts, dead_lettered_at, reason)
                values (?, 'nats.listener', 'com.frappe.Probe', ?, ?, 'FAILED', 24, ?, 'MAX_ATTEMPTS_EXHAUSTED')
                """,
                id,
                "{\"eventId\":\"" + id + "\"}",
                Timestamp.from(Instant.parse("2026-08-01T12:00:00Z")),
                Timestamp.from(Instant.parse("2026-08-01T12:00:00Z")));
        return id;
    }

    private void insertTraceContext(UUID eventId) {
        jdbc.update(
                """
                insert into platform.event_trace_context (event_id, traceparent, tracestate, recorded_at)
                values (?, ?, '', ?)
                """,
                eventId,
                "00-" + eventId.toString().replace("-", "") + "0000-0102030405060708-01",
                Timestamp.from(Instant.parse("2026-08-01T12:00:00Z")));
    }

    private int archiveCount(UUID eventId) {
        return count("platform.event_publication_archive", eventId);
    }

    private int traceContextCount(UUID eventId) {
        var rows = jdbc.queryForObject(
                "select count(*) from platform.event_trace_context where event_id = ?", Integer.class, eventId);
        return rows == null ? 0 : rows;
    }

    private int count(String table, UUID eventId) {
        var rows = jdbc.queryForObject("select count(*) from " + table + " where id = ?", Integer.class, eventId);
        return rows == null ? 0 : rows;
    }
}
