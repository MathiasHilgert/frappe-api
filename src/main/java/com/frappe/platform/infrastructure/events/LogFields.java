package com.frappe.platform.infrastructure.events;

/**
 * Names of the structured log fields (SLF4J key/values) this package emits, namespaced under {@code frappe.outbox}.
 * ECS output nests them by dot; stable for log queries.
 */
final class LogFields {

    /** Pause until the next resubmission run, ISO-8601 duration. */
    static final String RECOVERY_INTERVAL = "frappe.outbox.recovery_interval";

    /** Most publications resubmitted per recovery run. */
    static final String BATCH_SIZE = "frappe.outbox.batch_size";

    private LogFields() {}
}
