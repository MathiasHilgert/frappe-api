package com.frappe.platform.infrastructure.nats;

/** Names of the structured log fields (SLF4J key/values) this package emits; stable for log queries. */
final class LogFields {

    /** NATS server URL. */
    static final String NATS_URL = "natsUrl";

    /** JetStream stream name. */
    static final String STREAM = "stream";

    /** NATS subject. */
    static final String SUBJECT = "subject";

    /** Domain event id ({@code Nats-Msg-Id}). */
    static final String EVENT_ID = "eventId";

    /** Connection lifecycle event reported by jnats. */
    static final String CONNECTION_EVENT = "connectionEvent";

    private LogFields() {}
}
