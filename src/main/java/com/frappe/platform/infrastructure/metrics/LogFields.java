package com.frappe.platform.infrastructure.metrics;

/** Names of the structured log fields this package emits (see the NATS package for the naming rules). */
final class LogFields {

    /** Full business metric name. */
    static final String METRIC = "frappe.metric";

    /** Domain event class name. */
    static final String EVENT_TYPE = "frappe.event_type";

    /** Domain event id. */
    static final String EVENT_ID = "frappe.event_id";

    private LogFields() {}
}
