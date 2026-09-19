package com.frappe.platform.infrastructure.tracing;

import io.micrometer.observation.Observation;
import io.micrometer.observation.transport.Kind;
import java.util.Objects;
import java.util.Optional;

/**
 * Observation context of a messaging span (publish or process) that <em>links</em> to the creation context of the
 * message instead of continuing its trace: delivery through the outbox and NATS is delayed, retried and at least once,
 * so a parent-child relation would stretch the producing trace over hours and attach every redelivery to it.
 *
 * <p>Deliberately not a {@code SenderContext} or {@code ReceiverContext}: Spring Boot's handlers for those would
 * inject the publish span into the message, or make the received context the parent. {@link
 * LinkedMessageTracingHandler} creates the span instead.
 */
public final class LinkedMessageContext extends Observation.Context {

    private final Kind kind;
    private final W3cTraceContext creationContext;

    private LinkedMessageContext(Kind kind, Optional<W3cTraceContext> creationContext) {
        this.kind = Objects.requireNonNull(kind, "kind must not be null");
        this.creationContext = creationContext.orElse(null);
    }

    /**
     * The context of publishing a message.
     *
     * @param creationContext the trace context the message was created in, if any
     * @return a context for a PRODUCER span
     */
    public static LinkedMessageContext producer(Optional<W3cTraceContext> creationContext) {
        return new LinkedMessageContext(Kind.PRODUCER, creationContext);
    }

    /**
     * The context of processing a received message.
     *
     * @param creationContext the trace context the message was created in, if it carried a valid one
     * @return a context for a CONSUMER span
     */
    public static LinkedMessageContext consumer(Optional<W3cTraceContext> creationContext) {
        return new LinkedMessageContext(Kind.CONSUMER, creationContext);
    }

    /**
     * The span kind.
     *
     * @return {@link Kind#PRODUCER} or {@link Kind#CONSUMER}
     */
    public Kind getKind() {
        return kind;
    }

    /**
     * The trace context the span links to.
     *
     * @return the creation context of the message, or empty when it carried none
     */
    public Optional<W3cTraceContext> getCreationContext() {
        return Optional.ofNullable(creationContext);
    }
}
