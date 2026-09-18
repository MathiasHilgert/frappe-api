package com.frappe.platform.infrastructure.events;

import java.time.Duration;
import org.springframework.boot.context.properties.ConfigurationProperties;
import org.springframework.boot.context.properties.bind.DefaultValue;

/**
 * {@code frappe.outbox.recovery.*}, the single source of defaults for resubmitting failed publications.
 *
 * @param interval pause between two resubmission runs, measured from the end of the previous run
 * @param batchSize most publications resubmitted per run, and most kept in flight at once; bounds the load a
 *     recovering NATS receives after an outage
 */
@ConfigurationProperties("frappe.outbox.recovery")
record OutboxRecoveryProperties(
        @DefaultValue("1m") Duration interval,
        @DefaultValue("100") int batchSize) {

    /**
     * Validates the settings so a misconfiguration fails at startup, not in the first scheduled run.
     *
     * @param interval pause between runs; must be positive
     * @param batchSize publications per run; must be positive
     */
    OutboxRecoveryProperties {
        if (interval.isNegative() || interval.isZero()) {
            throw new IllegalArgumentException("frappe.outbox.recovery.interval must be positive, was " + interval);
        }
        if (batchSize <= 0) {
            throw new IllegalArgumentException("frappe.outbox.recovery.batch-size must be positive, was " + batchSize);
        }
    }
}
