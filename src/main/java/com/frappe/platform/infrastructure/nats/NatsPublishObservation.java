package com.frappe.platform.infrastructure.nats;

import com.frappe.platform.DomainEvent;
import com.frappe.platform.infrastructure.tracing.LinkedMessageContext;
import com.frappe.platform.infrastructure.tracing.W3cTraceContext;
import io.micrometer.observation.Observation;
import io.micrometer.observation.ObservationRegistry;
import java.util.Optional;

/**
 * The observation around one JetStream publish: a PRODUCER span and the {@code nats.publish} timer. Key names follow
 * the OpenTelemetry messaging semantic conventions. The subject is low cardinality (one per event type and version) and
 * tags the timer; the event id is high cardinality and stays a span attribute. The span is a child of the current span
 * (the relay listener, or the recovery pass on a resubmission) and links to the trace the event was recorded in.
 */
final class NatsPublishObservation {

    /** Observation name; the timer is exported as {@code nats.publish}. */
    static final String NAME = "nats.publish";

    private NatsPublishObservation() {}

    /**
     * Creates the observation for publishing an event; the caller starts and stops it.
     *
     * @param registry where the observation is recorded
     * @param subject target subject
     * @param event the event being published
     * @param creationContext the trace context the event was recorded in, if any
     * @return the not yet started observation
     */
    static Observation of(
            ObservationRegistry registry,
            String subject,
            DomainEvent event,
            Optional<W3cTraceContext> creationContext) {
        return Observation.createNotStarted(NAME, () -> LinkedMessageContext.producer(creationContext), registry)
                .contextualName(MessagingObservationKeys.PUBLISH + " " + subject)
                .lowCardinalityKeyValue(MessagingObservationKeys.OPERATION_TYPE, MessagingObservationKeys.SEND)
                .lowCardinalityKeyValue(MessagingObservationKeys.OPERATION_NAME, MessagingObservationKeys.PUBLISH)
                .lowCardinalityKeyValue(MessagingObservationKeys.MESSAGING_SYSTEM, MessagingObservationKeys.NATS)
                .lowCardinalityKeyValue(MessagingObservationKeys.DESTINATION, subject)
                .highCardinalityKeyValue(
                        MessagingObservationKeys.MESSAGE_ID, event.eventId().toString());
    }
}
