package com.frappe.platform.infrastructure.scheduling;

import java.time.Duration;
import org.springframework.boot.context.properties.ConfigurationProperties;
import org.springframework.boot.context.properties.bind.DefaultValue;

/**
 * {@code frappe.scheduling.*}, the single source of defaults for retrying failed scheduled tasks. The scheduler itself
 * is configured under {@code db-scheduler.*} (application.properties).
 *
 * @param initialBackoff wait before the first retry of a failed execution; doubles with every further failure
 * @param maxRetries retries with backoff (30s, 1m, 2m, 4m, 8m with the defaults) before a recurring task falls back to
 *     its schedule and a one-time task is given up
 */
@ConfigurationProperties("frappe.scheduling")
record SchedulingProperties(
        @DefaultValue("30s") Duration initialBackoff,
        @DefaultValue("5") int maxRetries) {

    // 2^20 steps of the initial backoff already exceed any sensible wait; the bound keeps the arithmetic exact.
    private static final int MAX_RETRIES_LIMIT = 20;

    /**
     * Validates the settings so a misconfiguration fails at startup, not with the first failing task.
     *
     * @param initialBackoff wait before the first retry; must be positive
     * @param maxRetries retries with backoff; between 0 and 20
     */
    SchedulingProperties {
        if (initialBackoff.isNegative() || initialBackoff.isZero()) {
            throw new IllegalArgumentException(
                    "frappe.scheduling.initial-backoff must be positive, was " + initialBackoff);
        }
        if (maxRetries < 0 || maxRetries > MAX_RETRIES_LIMIT) {
            throw new IllegalArgumentException(
                    "frappe.scheduling.max-retries must be between 0 and " + MAX_RETRIES_LIMIT + ", was " + maxRetries);
        }
    }
}
