package com.frappe.platform.infrastructure.events;

import io.micrometer.observation.ObservationRegistry;
import java.time.Clock;
import java.util.function.IntUnaryOperator;

/**
 * One scheduled purge run over the outbox archive: deletes archived publications completed more than {@code
 * frappe.outbox.archive.retention} ago, then trace context rows no longer needed by any outbox table, both in batches
 * of {@code frappe.outbox.archive.purge-batch-size} so the purge never holds one long-running transaction or lock.
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
     * Runs one purge and observes it.
     *
     * @throws org.springframework.dao.DataAccessException if the database fails; the scheduler retries the run with
     *     backoff, never later than the next regular run
     */
    void purge() {
        var observation = OutboxPurgeObservations.purge(observations);
        // observe() records a failure on the observation and rethrows it: the scheduler retries with backoff and its
        // failure handler logs it once, so nothing here catches or logs.
        observation.observe(() -> {
            var archived = deleteInBatches(this::purgeArchiveBatch);
            var traceContext = deleteInBatches(repository::purgeOrphanTraceContext);
            observation.highCardinalityKeyValue(OutboxPurgeObservations.ARCHIVED_COUNT, String.valueOf(archived));
            observation.highCardinalityKeyValue(
                    OutboxPurgeObservations.TRACE_CONTEXT_COUNT, String.valueOf(traceContext));
        });
    }

    private int purgeArchiveBatch(int batchSize) {
        return repository.purgeArchivedBefore(clock.instant().minus(properties.retention()), batchSize);
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
