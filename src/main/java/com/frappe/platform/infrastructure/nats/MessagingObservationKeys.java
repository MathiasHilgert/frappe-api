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

    /** Low-cardinality key: the operation type, {@link #SEND} or {@link #PROCESS}. */
    static final String OPERATION_TYPE = "messaging.operation.type";

    /** Low-cardinality key: the system-specific operation name, {@link #PUBLISH} or {@link #PROCESS}. */
    static final String OPERATION_NAME = "messaging.operation.name";

    /** Operation type of handing a message to the broker. */
    static final String SEND = "send";

    /** Operation name of a JetStream publish; also the first word of the span name. */
    static final String PUBLISH = "publish";

    /** Operation type and name of processing a received message; also the first word of the span name. */
    static final String PROCESS = "process";

    /** Value of {@link #MESSAGING_SYSTEM}. */
    static final String NATS = "nats";

    private MessagingObservationKeys() {}
}
