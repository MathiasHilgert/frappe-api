package com.frappe.platform.infrastructure.events;

/**
 * Names of the structured log fields (SLF4J key/values) this package emits, namespaced under {@code frappe.outbox}.
 * ECS output nests them by dot; stable for log queries.
 */
final class LogFields {

    /** Id of the publication (registry row), not of the event. */
    static final String PUBLICATION_ID = "frappe.outbox.publication_id";

    /** Fully qualified class name of the event. */
    static final String EVENT_TYPE = "frappe.outbox.event_type";

    /** Listener the publication targets. */
    static final String LISTENER_ID = "frappe.outbox.listener_id";

    /** Attempts made so far. */
    static final String COMPLETION_ATTEMPTS = "frappe.outbox.completion_attempts";

    /** Why a publication became a dead letter. */
    static final String DEAD_LETTER_REASON = "frappe.outbox.dead_letter_reason";

    private LogFields() {}
}
