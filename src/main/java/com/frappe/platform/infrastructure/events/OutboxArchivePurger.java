package com.frappe.platform.infrastructure.events;

import io.micrometer.observation.ObservationRegistry;
import java.time.Clock;
import java.time.Instant;
import java.util.HashSet;
import java.util.List;
import java.util.UUID;
import java.util.function.IntUnaryOperator;

/**
 * One scheduled purge run over the outbox archive: deletes archived publications completed more than {@code
 * frappe.outbox.archive.retention} ago, then their trace context, in batches of {@code
 * frappe.outbox.archive.purge-batch-size} so the purge never holds one long-running transaction or lock. Trace
 * context is purged in two passes: driven from every archive batch just deleted (the common case), then a bounded
 * sweep for rows left behind without one (see {@link OutboxArchivePurgeRepository}).
 *
 * <p>Runs as the one cluster-wide execution of {@link OutboxArchivePurgeTask} (db-scheduler), on a fixed delay, so
 * purges never overlap on one instance or across instances.
 */
final class OutboxArchivePurger {

    private final OutboxArchivePurgeRepository repository;
    private final OutboxArchivePurgeProperties properties;
    private final Clock clock;
    private final ObservationRegistry observations;

    /**
     * Creates the purger.
     *
     * @param repository purge queries on the outbox archive
     * @param properties purge settings
     * @param clock the application clock
     * @param observations records every run ({@link OutboxPurgeObservations})
     */
    OutboxArchivePurger(
            OutboxArchivePurgeRepository repository,
            OutboxArchivePurgeProperties properties,
            Clock clock,
            ObservationRegistry observations) {
        this.repository = repository;
        this.properties = properties;
        this.clock = clock;
        this.observations = observations;
    }

    /**
     * Runs one purge and observes it. The purged row counts are recorded on the observation even when a later phase
     * fails, so a partial run is still visible.
     *
     * @throws org.springframework.dao.DataAccessException if the database fails; the scheduler retries the run with
     *     backoff, never later than the next regular run
     */
    void purge() {
        var observation = OutboxPurgeObservations.purge(observations).start();
        var archived = 0;
        var traceContext = 0;
        try {
            var threshold = clock.instant().minus(properties.retention());
            var batchSize = properties.purgeBatchSize();
            List<UUID> batch;
            do {
                batch = repository.purgeArchivedBefore(threshold, batchSize);
                archived += batch.size();
                // A row whose generated event_id is null (platform.safe_event_id could not parse serialized_event,
                // e.g. a malformed or hand-edited row) never blocks the run: it was still purged from the archive
                // above, just skipped here — there is no readable event id to purge trace context for.
                var readableEventIds = new HashSet<UUID>(batch);
                readableEventIds.remove(null);
                if (!readableEventIds.isEmpty()) {
                    traceContext += repository.purgeTraceContextFor(readableEventIds);
                }
            } while (batch.size() == batchSize);
            traceContext += deleteOrphanTraceContextInBatches(threshold);
        } catch (RuntimeException e) {
            // Never caught to log: the scheduler retries with backoff and its failure handler logs it once.
            observation.error(e);
            throw e;
        } finally {
            observation.highCardinalityKeyValue(OutboxPurgeObservations.ARCHIVED_COUNT, String.valueOf(archived));
            observation.highCardinalityKeyValue(
                    OutboxPurgeObservations.TRACE_CONTEXT_COUNT, String.valueOf(traceContext));
            observation.stop();
        }
    }

    private int deleteOrphanTraceContextInBatches(Instant threshold) {
        return deleteInBatches(batchSize -> repository.purgeOrphanTraceContext(threshold, batchSize));
    }

    // Keeps deleting until a batch comes back smaller than the batch size (or empty): every batch is its own small
    // statement, never one long-running transaction or lock.
    private int deleteInBatches(IntUnaryOperator deleteBatch) {
        var batchSize = properties.purgeBatchSize();
        var total = 0;
        int deleted;
        do {
            deleted = deleteBatch.applyAsInt(batchSize);
            total += deleted;
        } while (deleted == batchSize);
        return total;
    }
}
