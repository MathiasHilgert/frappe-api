package com.frappe.platform.infrastructure.events;

import java.sql.Timestamp;
import java.time.Instant;
import java.util.Collection;
import java.util.List;
import java.util.UUID;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.stereotype.Repository;

/**
 * Purge queries on the outbox archive: batched deletes so the purge never holds one long-running transaction or lock,
 * matching a publication's {@code eventId} by equality against the generated, indexed {@code event_id} column of
 * {@code event_publication}, {@code event_publication_archive} and {@code event_publication_dead_letter}
 * (migration {@code V202609191930}), never by scanning {@code serialized_event}.
 */
@Repository
class OutboxArchivePurgeRepository {

    private static final String DELETE_ARCHIVE_BATCH = """
            delete from platform.event_publication_archive
             where id in (
                 select id
                   from platform.event_publication_archive
                  where completion_date < ?
                  order by completion_date
                  limit ?)
            returning event_id
            """;

    // Driven from the archive batch just deleted: the archive delete already committed within this transaction, so
    // the not-exists checks below see its effect. A trace context row may be purged only once its event is in none
    // of the three outbox tables (domain-events.md, "Trace context"), so a manually replayed dead letter or another
    // still-open listener row for the same event keeps it.
    private static final String DELETE_TRACE_CONTEXT_FOR_EVENTS = """
            delete from platform.event_trace_context t
             where t.event_id = any(?)
               and not exists (select 1 from platform.event_publication p where p.event_id = t.event_id)
               and not exists (select 1 from platform.event_publication_archive a where a.event_id = t.event_id)
               and not exists (select 1 from platform.event_publication_dead_letter d where d.event_id = t.event_id)
            """;

    // Bounded fallback sweep for trace context rows left behind without a matching archive delete this run (for
    // example purged before this indexed column existed). Every not-exists check is now an indexed equality lookup,
    // not a scan of serialized_event.
    private static final String DELETE_ORPHAN_TRACE_CONTEXT_BATCH = """
            delete from platform.event_trace_context
             where event_id in (
                 select t.event_id
                   from platform.event_trace_context t
                  where not exists (select 1 from platform.event_publication p where p.event_id = t.event_id)
                    and not exists (select 1 from platform.event_publication_archive a where a.event_id = t.event_id)
                    and not exists (
                            select 1 from platform.event_publication_dead_letter d where d.event_id = t.event_id)
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
     * Deletes one batch of archived publications completed before the threshold, oldest first.
     *
     * @param completedBefore only publications completed before this instant are deleted
     * @param batchSize most rows deleted by this call
     * @return the event ids of the deleted rows
     */
    List<UUID> purgeArchivedBefore(Instant completedBefore, int batchSize) {
        return jdbc.sql(DELETE_ARCHIVE_BATCH)
                .params(Timestamp.from(completedBefore), batchSize)
                .query((row, rowNumber) -> row.getObject("event_id", UUID.class))
                .list();
    }

    /**
     * Deletes the trace context of the given events if none of them is still needed by {@code event_publication},
     * {@code event_publication_archive} or {@code event_publication_dead_letter}.
     *
     * @param eventIds event ids just removed from the archive
     * @return the number of deleted rows
     */
    int purgeTraceContextFor(Collection<UUID> eventIds) {
        return jdbc.sql(DELETE_TRACE_CONTEXT_FOR_EVENTS)
                .param(eventIds.toArray(UUID[]::new))
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
