package com.frappe.platform.infrastructure.events;

import static org.assertj.core.api.Assertions.assertThat;

import com.frappe.TestcontainersConfiguration;
import java.sql.Timestamp;
import java.time.Duration;
import java.time.Instant;
import java.util.ArrayList;
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
import org.springframework.transaction.support.TransactionTemplate;

/** Acceptance tests of FAPI-9 against real Postgres: the purge deletes only what the retention rule allows. */
@SpringBootTest(properties = {"frappe.outbox.archive.purge-batch-size=2"})
@Import(TestcontainersConfiguration.class)
@ActiveProfiles("local")
class OutboxArchivePurgeIntegrationTests {

    @Autowired
    OutboxArchivePurger purger;

    @Autowired
    JdbcTemplate jdbc;

    @Autowired
    TransactionTemplate transactions;

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

    @Test
    void orphanedTraceContextLeftBehindByAnIncompleteRunIsRemovedByALaterRun() {
        // Given: an archive row already gone (as if a previous run purged it) but its trace context still there, as
        // if that run failed before reaching the trace context phase.
        var event = insertArchived(NOW.minus(Duration.ofDays(60)));
        insertTraceContext(event);
        jdbc.update("delete from platform.event_publication_archive where id = ?", event);

        // When: a later run's bounded orphan sweep picks it up.
        purger.purge();

        // Then
        assertThat(traceContextCount(event)).isZero();
    }

    // Proves the not-exists checks the repository's own SQL runs for every candidate trace context row can be served
    // by the generated event_id indexes (V202609191930__add_event_id_to_outbox_tables.sql), not by scanning
    // serialized_event on the (potentially huge) outbox tables. Seeded with a few thousand unrelated rows per table
    // and ANALYZEd first: at that scale Postgres' own cost model still prefers a hash anti join over seq-scanned
    // tables (verified: EXPLAIN without any override chooses Seq Scan on all three outbox tables here, because they
    // are still "small" by its cost model), so `enable_seqscan = off` stays: it does not fake an index that could not
    // otherwise serve the query, it only removes a cheaper-at-this-size alternative so the plan proves the index
    // exists and CAN serve it, the property that matters at production volume.
    @Test
    void theOrphanTraceContextSweepUsesTheEventIdIndexesInsteadOfScanningTheOutboxTables() {
        seedUnrelatedRows();

        var text = explain(OutboxArchivePurgeRepository.DELETE_ORPHAN_TRACE_CONTEXT_BATCH, Timestamp.from(NOW), 500);

        assertThat(text)
                .as("plan:%n%s", text)
                .contains("event_publication_event_id_idx")
                .contains("event_publication_archive_event_id_idx")
                .contains("event_publication_dead_letter_event_id_idx");
    }

    // Same proof for the batch-driven delete (the common case, run right after every archive batch): it also matches
    // by the indexed event_id, not by serialized_event.
    @Test
    void theArchiveBatchDrivenTraceContextDeleteUsesTheEventIdIndexes() {
        seedUnrelatedRows();

        var text = explain(OutboxArchivePurgeRepository.DELETE_TRACE_CONTEXT_FOR_EVENTS, (Object)
                IntStream.range(0, 50).mapToObj(i -> UUID.randomUUID()).toArray(UUID[]::new));

        assertThat(text)
                .as("plan:%n%s", text)
                .contains("event_publication_event_id_idx")
                .contains("event_publication_archive_event_id_idx")
                .contains("event_publication_dead_letter_event_id_idx");
    }

    private String explain(String sql, Object... params) {
        var plan = new ArrayList<String>();
        transactions.executeWithoutResult(status -> {
            jdbc.execute("set local enable_seqscan = off");
            plan.addAll(jdbc.queryForList("explain (format text)\n" + sql, String.class, params));
        });
        return String.join("\n", plan);
    }

    // A few thousand unrelated rows per outbox table (none of them the row under test), so the EXPLAIN tests above
    // exercise the same indexed not-exists lookups the repository runs in production, not an unrealistically tiny
    // table. Inserted in batches for speed.
    private void seedUnrelatedRows() {
        var pending = new ArrayList<Object[]>();
        var archive = new ArrayList<Object[]>();
        var deadLetter = new ArrayList<Object[]>();
        var trace = new ArrayList<Object[]>();
        for (var i = 0; i < 3000; i++) {
            var pendingId = UUID.randomUUID();
            pending.add(new Object[] {pendingId, "{\"eventId\":\"" + UUID.randomUUID() + "\"}", Timestamp.from(NOW)});
            var archiveId = UUID.randomUUID();
            archive.add(new Object[] {
                archiveId, "{\"eventId\":\"" + UUID.randomUUID() + "\"}", Timestamp.from(NOW), Timestamp.from(NOW)
            });
            var deadLetterId = UUID.randomUUID();
            deadLetter.add(new Object[] {
                deadLetterId, "{\"eventId\":\"" + UUID.randomUUID() + "\"}", Timestamp.from(NOW), Timestamp.from(NOW)
            });
            trace.add(
                    new Object[] {UUID.randomUUID(), "00-seed-01", "", Timestamp.from(NOW.minus(Duration.ofDays(60)))});
        }
        jdbc.batchUpdate("""
                insert into platform.event_publication (id, listener_id, event_type, serialized_event,
                    publication_date, status, completion_attempts)
                values (?, 'nats.listener', 'com.frappe.Probe', ?, ?, 'FAILED', 1)
                """, pending);
        jdbc.batchUpdate("""
                insert into platform.event_publication_archive (id, listener_id, event_type, serialized_event,
                    publication_date, completion_date, status, completion_attempts)
                values (?, 'nats.listener', 'com.frappe.Probe', ?, ?, ?, 'COMPLETED', 1)
                """, archive);
        jdbc.batchUpdate("""
                insert into platform.event_publication_dead_letter (id, listener_id, event_type, serialized_event,
                    publication_date, status, completion_attempts, dead_lettered_at, reason)
                values (?, 'nats.listener', 'com.frappe.Probe', ?, ?, 'FAILED', 24, ?, 'MAX_ATTEMPTS_EXHAUSTED')
                """, deadLetter);
        jdbc.batchUpdate("""
                insert into platform.event_trace_context (event_id, traceparent, tracestate, recorded_at)
                values (?, ?, ?, ?)
                """, trace);
        jdbc.execute("analyze platform.event_publication, platform.event_publication_archive,"
                + " platform.event_publication_dead_letter, platform.event_trace_context");
    }

    // The generated event_id column must never fail the insert it derives from, or a corrupted or hand-edited row
    // would roll back the business transaction that wrote it (or, for a hand-inserted dead letter, block a replay).
    // No path this application controls can produce either case (eventId is a mandatory, injected-UUID DomainEvent
    // field), but the guarantee is unconditional: platform.safe_event_id (V202609191930) returns null instead of
    // raising for malformed JSON or a non-UUID eventId.
    @Test
    void aRowWithMalformedJsonOrANonUuidEventIdStillInsertsWithANullGeneratedEventId() {
        // Given / When
        var malformedJson = UUID.randomUUID();
        jdbc.update("""
                insert into platform.event_publication_archive (id, listener_id, event_type, serialized_event,
                    publication_date, completion_date, status, completion_attempts)
                values (?, 'nats.listener', 'com.frappe.Probe', ?, ?, ?, 'COMPLETED', 1)
                """, malformedJson, "{\"eventId\": ", Timestamp.from(NOW), Timestamp.from(NOW));

        var nonUuidEventId = UUID.randomUUID();
        jdbc.update("""
                insert into platform.event_publication_archive (id, listener_id, event_type, serialized_event,
                    publication_date, completion_date, status, completion_attempts)
                values (?, 'nats.listener', 'com.frappe.Probe', ?, ?, ?, 'COMPLETED', 1)
                """, nonUuidEventId, "{\"eventId\":\"not-a-uuid\"}", Timestamp.from(NOW), Timestamp.from(NOW));

        // Then
        assertThat(generatedEventId(malformedJson)).isNull();
        assertThat(generatedEventId(nonUuidEventId)).isNull();
    }

    private UUID generatedEventId(UUID id) {
        return jdbc.queryForObject(
                "select event_id from platform.event_publication_archive where id = ?", UUID.class, id);
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
