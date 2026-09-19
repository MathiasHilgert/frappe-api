package com.frappe.platform.infrastructure.nats;

import com.frappe.platform.DomainEvent;
import io.micrometer.observation.Observation;
import io.micrometer.observation.ObservationRegistry;

/**
 * The observation around one JetStream publish: a span and the {@code nats.publish} timer. Key names follow the
 * OpenTelemetry messaging semantic conventions. The subject is low cardinality (one per event type and version) and
 * tags the timer; the event id is high cardinality and stays a span attribute.
 */
final class NatsPublishObservation {

    /** Observation name; the timer is exported as {@code nats.publish}. */
    static final String NAME = "nats.publish";

    /** Low-cardinality key: the messaging system, always {@code nats}. */
    static final String MESSAGING_SYSTEM = "messaging.system";

    /** Low-cardinality key: the NATS subject. */
    static final String DESTINATION = "messaging.destination.name";

    /** High-cardinality key: the domain event id ({@code Nats-Msg-Id}). */
    static final String MESSAGE_ID = "messaging.message.id";

    private static final String NATS = "nats";

    private NatsPublishObservation() {}

    /**
     * Creates the observation for publishing an event; the caller starts and stops it.
     *
     * @param registry where the observation is recorded
     * @param subject target subject
     * @param event the event being published
     * @return the not yet started observation
     */
    static Observation of(ObservationRegistry registry, String subject, DomainEvent event) {
        return Observation.createNotStarted(NAME, registry)
                .contextualName("publish " + subject)
                .lowCardinalityKeyValue(MESSAGING_SYSTEM, NATS)
                .lowCardinalityKeyValue(DESTINATION, subject)
                .highCardinalityKeyValue(MESSAGE_ID, event.eventId().toString());
    }
}
