package com.frappe.platform.infrastructure.nats;

import com.frappe.platform.DomainEvent;
import com.frappe.platform.infrastructure.tracing.EventTraceContexts;
import com.frappe.platform.infrastructure.tracing.W3cTraceContext;
import io.micrometer.observation.ObservationRegistry;
import io.nats.client.JetStreamApiException;
import io.nats.client.JetStreamOptions;
import io.nats.client.impl.Headers;
import io.nats.client.support.NatsJetStreamConstants;
import java.io.IOException;
import java.time.Duration;
import java.util.Optional;
import java.util.concurrent.CompletableFuture;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.modulith.events.RoutingTarget;
import org.springframework.modulith.events.support.EventExternalizationTransport;
import tools.jackson.core.JacksonException;
import tools.jackson.databind.json.JsonMapper;

/**
 * Publishes a {@link DomainEvent} to JetStream and completes only after the ack, so the publication registry marks the
 * event published only once the stream stored it. {@code Nats-Msg-Id} is the event id: re-publishing within the
 * stream's duplicate window is stored once. The message carries the W3C trace context the event was recorded in
 * ({@code traceparent}, {@code tracestate}) on every publish, resubmissions included, and none if it was recorded
 * without a trace.
 */
class NatsEventTransport implements EventExternalizationTransport {

    /** Header: event type, {@code <module>.<event-kebab>}. */
    static final String EVENT_TYPE = "Frappe-Event-Type";

    /** Header: payload schema version. */
    static final String EVENT_VERSION = "Frappe-Event-Version";

    /** Header: id of the changed aggregate. */
    static final String AGGREGATE_ID = "Frappe-Aggregate-Id";

    /** Header: aggregate version after the change. */
    static final String AGGREGATE_VERSION = "Frappe-Aggregate-Version";

    /** Header: ISO-8601 UTC instant of the change. */
    static final String OCCURRED_AT = "Frappe-Occurred-At";

    private static final Logger log = LoggerFactory.getLogger(NatsEventTransport.class);

    private final NatsClient client;
    private final JetStreamOptions options;
    private final JsonMapper json;
    private final ObservationRegistry observations;
    private final EventTraceContexts traceContexts;

    /**
     * Creates the transport.
     *
     * @param client owner of the connection
     * @param publishTimeout how long to wait for the JetStream ack
     * @param json payload serializer
     * @param observations records every publish as a {@link NatsPublishObservation}
     * @param traceContexts the trace contexts events were recorded in
     */
    NatsEventTransport(
            NatsClient client,
            Duration publishTimeout,
            JsonMapper json,
            ObservationRegistry observations,
            EventTraceContexts traceContexts) {
        this.client = client;
        this.options = JetStreamOptions.builder().requestTimeout(publishTimeout).build();
        this.json = json;
        this.observations = observations;
        this.traceContexts = traceContexts;
    }

    @Override
    public CompletableFuture<?> externalize(Object payload, RoutingTarget target) {
        var event = (DomainEvent) payload;
        var subject = target.getTarget();
        var creationContext = traceContexts.recordedFor(event.eventId());
        var observation = NatsPublishObservation.of(observations, subject, event, creationContext)
                .start();
        try (var scope = observation.openScope()) {
            var ack = client.publish(subject, headers(event, creationContext), json.writeValueAsBytes(event), options);
            log.atDebug()
                    .addKeyValue(LogFields.EVENT_ID, event.eventId())
                    .addKeyValue(LogFields.SUBJECT, subject)
                    .log(
                            "Published {} (seq {}, duplicate {})",
                            event.getClass().getSimpleName(),
                            ack.getSeqno(),
                            ack.isDuplicate());
            return CompletableFuture.completedFuture(ack);
        } catch (IOException | JetStreamApiException | NatsUnavailableException | JacksonException e) {
            observation.error(e);
            // The failed future is the result, not a rethrow: this is the one place with the event context to log.
            var failure = new EventPublicationException(
                    "Publishing " + event.getClass().getSimpleName() + " " + event.eventId() + " to " + subject
                            + " failed; the publication stays incomplete for retry",
                    e);
            log.atWarn()
                    .addKeyValue(LogFields.EVENT_ID, event.eventId())
                    .addKeyValue(LogFields.SUBJECT, subject)
                    .setCause(e)
                    .log("{}", failure.getMessage());
            return CompletableFuture.failedFuture(failure);
        } finally {
            observation.stop();
        }
    }

    private static Headers headers(DomainEvent event, Optional<W3cTraceContext> creationContext) {
        var headers = new Headers()
                .put(NatsJetStreamConstants.MSG_ID_HDR, event.eventId().toString())
                .put(EVENT_TYPE, NatsSubjects.eventType(event.getClass()))
                .put(EVENT_VERSION, String.valueOf(event.eventVersion()))
                .put(AGGREGATE_ID, event.aggregateId().toString())
                .put(AGGREGATE_VERSION, String.valueOf(event.aggregateVersion()))
                .put(OCCURRED_AT, event.occurredAt().toString());
        creationContext.ifPresent(context -> {
            headers.put(W3cTraceContext.TRACEPARENT, context.traceparent());
            // An empty tracestate is the same as none (W3C); an empty header would only add noise.
            if (!context.tracestate().isEmpty()) {
                headers.put(W3cTraceContext.TRACESTATE, context.tracestate());
            }
        });
        return headers;
    }
}
