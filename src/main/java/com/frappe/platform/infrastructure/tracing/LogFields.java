package com.frappe.platform.infrastructure.tracing;

/**
 * Names of the structured log fields (SLF4J key/values) this package emits. ECS output nests them by dot; stable for
 * log queries.
 */
final class LogFields {

    /** Domain event id. */
    static final String EVENT_ID = "frappe.event_id";

    private LogFields() {}
}
