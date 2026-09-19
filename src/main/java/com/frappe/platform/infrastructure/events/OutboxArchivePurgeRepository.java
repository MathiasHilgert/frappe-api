package com.frappe.platform.infrastructure.events;

import java.sql.Timestamp;
import java.time.Instant;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.stereotype.Repository;

/**
 * Purge queries on the outbox archive: batched deletes so the purge never holds one long-running transaction or lock.
 */
@Repository
class OutboxArchivePurgeRepository {

    private static final String DELETE_ARCHIVE_BATCH = """
            delete from platform.event_publication_archive
             where id in (
                 select id
                   from platform.event_publication_archive
                  where completion_date < ?
                  limit ?)
            """;

    // A trace context row may be purged only once its event id (embedded in serialized_event, not the row id: one
    // event can have several publication rows, one per listener) is in none of the three outbox tables, so a
    // manually replayed dead letter still carries its original context (domain-events.md, "Trace context").
    private static final String DELETE_ORPHAN_TRACE_CONTEXT_BATCH = """
            delete from platform.event_trace_context
             where event_id in (
                 select t.event_id
                   from platform.event_trace_context t
                  where not exists (
                          select 1 from platform.event_publication p
                           where p.serialized_event like '%' || t.event_id || '%')
                    and not exists (
                          select 1 from platform.event_publication_archive a
                           where a.serialized_event like '%' || t.event_id || '%')
                    and not exists (
                          select 1 from platform.event_publication_dead_letter d
                           where d.serialized_event like '%' || t.event_id || '%')
                  limit ?)
            """;

    private final JdbcClient jdbc;

    /**
     * Creates the repository.
     *
     * @param jdbc client on the application data source
     */
    OutboxArchivePurgeRepository(JdbcClient jdbc) {
        this.jdbc = jdbc;
    }

    /**
     * Deletes one batch of archived publications completed before the threshold.
     *
     * @param completedBefore only publications completed before this instant are deleted
     * @param batchSize most rows deleted by this call
     * @return the number of deleted rows
     */
    int purgeArchivedBefore(Instant completedBefore, int batchSize) {
        return jdbc.sql(DELETE_ARCHIVE_BATCH)
                .params(Timestamp.from(completedBefore), batchSize)
                .update();
    }

    /**
     * Deletes one batch of trace context rows whose event is in none of {@code event_publication}, {@code
     * event_publication_archive} and {@code event_publication_dead_letter}.
     *
     * @param batchSize most rows deleted by this call
     * @return the number of deleted rows
     */
    int purgeOrphanTraceContext(int batchSize) {
        return jdbc.sql(DELETE_ORPHAN_TRACE_CONTEXT_BATCH).params(batchSize).update();
    }
}
