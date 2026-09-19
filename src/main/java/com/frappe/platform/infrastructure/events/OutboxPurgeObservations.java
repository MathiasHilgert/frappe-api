package com.frappe.platform.infrastructure.events;

import io.micrometer.observation.Observation;
import io.micrometer.observation.ObservationRegistry;

/**
 * The observation of one archive purge run ({@code outbox.purge}): a span and timer, with the purged row counts as
 * span attributes (unbounded per run, so not timer tags).
 */
final class OutboxPurgeObservations {

    /** Observation name of one purge run. */
    static final String PURGE = "outbox.purge";

    /** High-cardinality key: archived publications deleted this run. */
    static final String ARCHIVED_COUNT = "outbox.purge.archived.count";

    /** High-cardinality key: orphaned trace context rows deleted this run. */
    static final String TRACE_CONTEXT_COUNT = "outbox.purge.trace-context.count";

    private OutboxPurgeObservations() {}

    /**
     * Creates the observation of one purge run; the caller starts, tags and stops it.
     *
     * @param registry where the observation is recorded
     * @return the not yet started observation
     */
    static Observation purge(ObservationRegistry registry) {
        return Observation.createNotStarted(PURGE, registry).contextualName("outbox purge");
    }
}
