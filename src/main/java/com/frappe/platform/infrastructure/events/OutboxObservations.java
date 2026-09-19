package com.frappe.platform.infrastructure.events;

import com.frappe.platform.infrastructure.events.PublicationRedelivery.Outcome;
import io.micrometer.observation.Observation;
import io.micrometer.observation.ObservationRegistry;
import java.util.Locale;

/**
 * The observations of the outbox recovery: a span and timer per pass ({@code outbox.recovery}) and per resubmitted
 * publication ({@code outbox.redelivery}). Trigger and outcome are bounded enums and tag the timers; the publication id
 * is high cardinality and stays a span attribute.
 */
final class OutboxObservations {

    /** Observation name of one recovery pass. */
    static final String RECOVERY = "outbox.recovery";

    /** Low-cardinality key: what started the pass. */
    static final String TRIGGER = "outbox.recovery.trigger";

    /** Observation name of one publication handed to {@link PublicationRedelivery}. */
    static final String REDELIVERY = "outbox.redelivery";

    /** Low-cardinality key: the {@link Outcome}, lowercase. */
    static final String OUTCOME = "outbox.redelivery.outcome";

    /** High-cardinality key: the publication id. */
    static final String PUBLICATION_ID = "outbox.publication.id";

    /** What started a recovery pass. */
    enum Trigger {

        /** The fixed-delay schedule. */
        SCHEDULED,

        /** A messaging transport came back. */
        TRANSPORT_RECOVERED;

        /**
         * The tag value.
         *
         * @return the lowercase name
         */
        String tag() {
            return name().toLowerCase(Locale.ROOT);
        }
    }

    private OutboxObservations() {}

    /**
     * Creates the observation of one recovery pass; the caller starts and stops it.
     *
     * @param registry where the observation is recorded
     * @param trigger what started the pass
     * @return the not yet started observation
     */
    static Observation recovery(ObservationRegistry registry, Trigger trigger) {
        return Observation.createNotStarted(RECOVERY, registry)
                .contextualName("outbox recovery")
                .lowCardinalityKeyValue(TRIGGER, trigger.tag());
    }

    /**
     * Creates the observation of one redelivery; the caller sets the outcome, starts and stops it.
     *
     * @param registry where the observation is recorded
     * @param publication the publication handed over
     * @return the not yet started observation
     */
    static Observation redelivery(ObservationRegistry registry, FailedPublication publication) {
        return Observation.createNotStarted(REDELIVERY, registry)
                .contextualName("outbox redelivery")
                .highCardinalityKeyValue(PUBLICATION_ID, publication.id().toString());
    }

    /**
     * The tag value of an outcome.
     *
     * @param outcome the redelivery outcome
     * @return the lowercase name
     */
    static String outcomeTag(Outcome outcome) {
        return outcome.name().toLowerCase(Locale.ROOT);
    }
}
