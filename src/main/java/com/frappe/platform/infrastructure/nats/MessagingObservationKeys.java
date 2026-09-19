package com.frappe.platform.infrastructure.nats;

/**
 * Key names shared by the messaging observations of this package, following the OpenTelemetry messaging semantic
 * conventions.
 */
final class MessagingObservationKeys {

    /** Low-cardinality key: the messaging system, always {@link #NATS}. */
    static final String MESSAGING_SYSTEM = "messaging.system";

    /** Low-cardinality key: the NATS subject. */
    static final String DESTINATION = "messaging.destination.name";

    /** High-cardinality key: the domain event id ({@code Nats-Msg-Id}). */
    static final String MESSAGE_ID = "messaging.message.id";

    /** Value of {@link #MESSAGING_SYSTEM}. */
    static final String NATS = "nats";

    private MessagingObservationKeys() {}
}
