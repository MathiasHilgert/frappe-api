package com.frappe.platform.infrastructure.events;

import java.time.Duration;
import org.springframework.boot.context.properties.ConfigurationProperties;
import org.springframework.boot.context.properties.bind.DefaultValue;

/**
 * {@code frappe.outbox.archive.*}, the single source of defaults for purging completed publications from {@code
 * platform.event_publication_archive} and their now-orphaned {@code platform.event_trace_context} rows.
 *
 * @param retention how long a completed publication stays in the archive before it is purged
 * @param purgeBatchSize most rows deleted per statement, so the purge never holds one long-running transaction or
 *     lock; the action keeps deleting batches until one comes back smaller than this
 * @param purgeInterval pause between two purge runs, measured from the end of the previous run
 */
@ConfigurationProperties("frappe.outbox.archive")
record OutboxArchivePurgeProperties(
        @DefaultValue("30d") Duration retention,
        @DefaultValue("500") int purgeBatchSize,
        @DefaultValue("1h") Duration purgeInterval) {

    /**
     * Validates the settings so a misconfiguration fails at startup, not in the first scheduled run.
     *
     * @param retention retention period; must be positive
     * @param purgeBatchSize rows deleted per statement; must be positive
     * @param purgeInterval pause between runs; must be positive
     */
    OutboxArchivePurgeProperties {
        requirePositive("retention", retention);
        requirePositive("purge-batch-size", purgeBatchSize);
        requirePositive("purge-interval", purgeInterval);
    }

    private static void requirePositive(String name, int value) {
        if (value <= 0) {
            throw new IllegalArgumentException("frappe.outbox.archive." + name + " must be positive, was " + value);
        }
    }

    private static void requirePositive(String name, Duration value) {
        if (value.isNegative() || value.isZero()) {
            throw new IllegalArgumentException("frappe.outbox.archive." + name + " must be positive, was " + value);
        }
    }
}
