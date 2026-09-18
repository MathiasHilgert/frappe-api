package com.frappe.platform.infrastructure.nats;

import com.frappe.platform.DomainEvent;
import io.nats.client.JetStreamApiException;
import io.nats.client.JetStreamOptions;
import io.nats.client.impl.Headers;
import io.nats.client.support.NatsJetStreamConstants;
import java.io.IOException;
import java.time.Duration;
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
 * stream's duplicate window is stored once.
 */
class NatsEventTransport implements EventExternalizationTransport {

    static final String EVENT_TYPE = "Frappe-Event-Type";
    static final String EVENT_VERSION = "Frappe-Event-Version";
    static final String AGGREGATE_ID = "Frappe-Aggregate-Id";
    static final String AGGREGATE_VERSION = "Frappe-Aggregate-Version";
    static final String OCCURRED_AT = "Frappe-Occurred-At";

    private static final Logger log = LoggerFactory.getLogger(NatsEventTransport.class);

    private final NatsClient client;
    private final JetStreamOptions options;
    private final JsonMapper json;

    NatsEventTransport(NatsClient client, Duration publishTimeout, JsonMapper json) {
        this.client = client;
        this.options = JetStreamOptions.builder().requestTimeout(publishTimeout).build();
        this.json = json;
    }

    @Override
    public CompletableFuture<?> externalize(Object payload, RoutingTarget target) {
        var event = (DomainEvent) payload;
        var subject = target.getTarget();
        try {
            var ack = client.connection()
                    .jetStream(options)
                    .publish(subject, headers(event), json.writeValueAsBytes(event));
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
            // The failed future is the result, not a rethrow: this is the one place with the event context to log.
            var failure = new EventPublicationException(
                    "Publishing " + event.getClass().getSimpleName() + " " + event.eventId() + " to " + subject
                            + " failed; the publication stays incomplete for retry",
                    e);
            log.atWarn()
                    .addKeyValue(LogFields.EVENT_ID, event.eventId())
                    .addKeyValue(LogFields.SUBJECT, subject)
                    .setCause(e)
                    .log(failure.getMessage());
            return CompletableFuture.failedFuture(failure);
        }
    }

    private static Headers headers(DomainEvent event) {
        return new Headers()
                .put(NatsJetStreamConstants.MSG_ID_HDR, event.eventId().toString())
                .put(EVENT_TYPE, NatsSubjects.eventType(event.getClass()))
                .put(EVENT_VERSION, String.valueOf(event.eventVersion()))
                .put(AGGREGATE_ID, event.aggregateId().toString())
                .put(AGGREGATE_VERSION, String.valueOf(event.aggregateVersion()))
                .put(OCCURRED_AT, event.occurredAt().toString());
    }
}
