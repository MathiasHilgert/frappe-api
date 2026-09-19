package com.frappe.platform.infrastructure.events;

import java.time.Duration;
import org.springframework.boot.context.properties.ConfigurationProperties;
import org.springframework.boot.context.properties.bind.DefaultValue;

/**
 * {@code frappe.outbox.recovery.*}, the single source of defaults for recovering publications that did not complete.
 *
 * @param interval pause between two recovery runs, measured from the end of the previous run
 * @param batchSize most publications resubmitted per run, and most kept in flight at once; bounds the load a
 *     recovering NATS receives after an outage
 * @param stuckAfter how long an attempt may run without outcome before it counts as stuck and is failed for retry;
 *     must exceed {@code frappe.nats.publish-timeout} plus the slowest listener, or a live attempt is retried twice
 * @param maxAttempts total attempts, the first publish included (Modulith stores {@code completion_attempts = 1} on
 *     publish and adds one per resubmission), after which a publication is moved to the dead-letter table
 * @param maxBackoff cap of the wait between attempts; the wait starts at {@code interval} and doubles per attempt,
 *     so with the defaults a publication is retried for about 18 hours before it becomes a dead letter
 */
@ConfigurationProperties("frappe.outbox.recovery")
record OutboxRecoveryProperties(
        @DefaultValue("1m") Duration interval,
        @DefaultValue("100") int batchSize,
        @DefaultValue("5m") Duration stuckAfter,
        @DefaultValue("24") int maxAttempts,
        @DefaultValue("1h") Duration maxBackoff) {

    /**
     * Validates the settings so a misconfiguration fails at startup, not in the first scheduled run.
     *
     * @param interval pause between runs; must be positive
     * @param batchSize publications per run; must be positive
     * @param stuckAfter attempt duration counted as stuck; must be positive
     * @param maxAttempts total attempts, the first publish included; must be positive
     * @param maxBackoff cap of the wait between attempts; must be positive and at least {@code interval}
     */
    OutboxRecoveryProperties {
        requirePositive("interval", interval);
        requirePositive("batch-size", batchSize);
        requirePositive("stuck-after", stuckAfter);
        requirePositive("max-attempts", maxAttempts);
        requirePositive("max-backoff", maxBackoff);
        if (maxBackoff.compareTo(interval) < 0) {
            throw new IllegalArgumentException("frappe.outbox.recovery.max-backoff (" + maxBackoff
                    + ") must be at least frappe.outbox.recovery.interval (" + interval + ")");
        }
    }

    private static void requirePositive(String name, int value) {
        if (value <= 0) {
            throw new IllegalArgumentException("frappe.outbox.recovery." + name + " must be positive, was " + value);
        }
    }

    private static void requirePositive(String name, Duration value) {
        if (value.isNegative() || value.isZero()) {
            throw new IllegalArgumentException("frappe.outbox.recovery." + name + " must be positive, was " + value);
        }
    }
}
