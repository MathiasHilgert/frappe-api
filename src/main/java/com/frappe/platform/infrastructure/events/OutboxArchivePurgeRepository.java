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
 * (migration {@code V202609191930}), never by scanning {@code serialized_event}. Package-visible for
 * {@code OutboxArchivePurgeIntegrationTests}, which runs these same SQL constants under {@code EXPLAIN}.
 */
@Repository
class OutboxArchivePurgeRepository {

    /**
     * Deletes one batch of archived publications completed before the threshold, oldest first, returning the
     * generated {@code event_id} of every deleted row (null for a row {@link #DELETE_ARCHIVE_BATCH}'s generated
     * column could not parse; see {@code platform.safe_event_id}).
     */
    static final String DELETE_ARCHIVE_BATCH = """
            delete from platform.event_publication_archive
             where id in (
                 select id
                   from platform.event_publication_archive
                  where completion_date < ?
                  order by completion_date
                  limit ?)
            returning event_id
            """;

    /**
     * Deletes the trace context of a given set of events if none of them is still needed by {@code
     * event_publication}, {@code event_publication_archive} or {@code event_publication_dead_letter}. Driven by the
     * caller from every archive batch {@link #DELETE_ARCHIVE_BATCH} just deleted: each delete statement runs and
     * autocommits on its own (there is no surrounding transaction), so this one sees the archive delete's effect,
     * committed moments before on the same connection.
     */
    static final String DELETE_TRACE_CONTEXT_FOR_EVENTS = """
            delete from platform.event_trace_context t
             where t.event_id = any(?)
               and not exists (select 1 from platform.event_publication p where p.event_id = t.event_id)
               and not exists (select 1 from platform.event_publication_archive a where a.event_id = t.event_id)
               and not exists (select 1 from platform.event_publication_dead_letter d where d.event_id = t.event_id)
            """;

    /**
     * Bounded fallback sweep for trace context rows left behind without a matching archive delete this run (for
     * example an earlier run interrupted between the archive delete and the trace context delete). Restricted to
     * {@code recorded_at} older than the retention (indexed, {@code V202609192010}): a leftover orphan can only be an
     * old row, since the common-case delete above only ever reaches rows the archive purge already aged out: this
     * keeps the sweep from walking every row in the table on every run. Every not-exists check is an indexed equality
     * lookup on the generated {@code event_id} columns, never a scan of {@code serialized_event}.
     */
    static final String DELETE_ORPHAN_TRACE_CONTEXT_BATCH = """
            delete from platform.event_trace_context
             where event_id in (
                 select t.event_id
                   from platform.event_trace_context t
                  where t.recorded_at < ?
                    and not exists (select 1 from platform.event_publication p where p.event_id = t.event_id)
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
     * @return the event ids of the deleted rows, one entry per row, possibly null (see {@link #DELETE_ARCHIVE_BATCH})
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
     * @param eventIds event ids just removed from the archive; never contains null
     * @return the number of deleted rows
     */
    int purgeTraceContextFor(Collection<UUID> eventIds) {
        return jdbc.sql(DELETE_TRACE_CONTEXT_FOR_EVENTS)
                .param(eventIds.toArray(UUID[]::new))
                .update();
    }

    /**
     * Deletes one batch of trace context rows recorded before the threshold whose event is in none of {@code
     * event_publication}, {@code event_publication_archive} and {@code event_publication_dead_letter}.
     *
     * @param recordedBefore only rows recorded before this instant are candidates
     * @param batchSize most rows deleted by this call
     * @return the number of deleted rows
     */
    int purgeOrphanTraceContext(Instant recordedBefore, int batchSize) {
        return jdbc.sql(DELETE_ORPHAN_TRACE_CONTEXT_BATCH)
                .params(Timestamp.from(recordedBefore), batchSize)
                .update();
    }
}
