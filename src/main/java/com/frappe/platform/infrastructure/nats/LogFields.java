package com.frappe.platform.infrastructure.nats;

/**
 * Names of the structured log fields (SLF4J key/values) this package emits: lowercase, dotted, namespaced ({@code
 * frappe.*} for domain values, {@code nats.*} for transport values). ECS output nests them by dot; stable for log
 * queries.
 */
final class LogFields {

    /** NATS server URL, always redacted ({@link NatsProperties#redactedUrl()}). */
    static final String NATS_URL = "nats.url";

    /** JetStream stream name. */
    static final String STREAM = "nats.stream";

    /** NATS subject. */
    static final String SUBJECT = "nats.subject";

    /** Domain event id ({@code Nats-Msg-Id}). */
    static final String EVENT_ID = "frappe.event_id";

    /** Connection lifecycle event reported by jnats. */
    static final String CONNECTION_EVENT = "nats.connection_event";

    private LogFields() {}
}
