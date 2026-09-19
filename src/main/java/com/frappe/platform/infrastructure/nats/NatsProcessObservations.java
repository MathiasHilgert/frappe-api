package com.frappe.platform.infrastructure.nats;

import com.frappe.platform.infrastructure.tracing.LinkedMessageContext;
import com.frappe.platform.infrastructure.tracing.W3cTraceContext;
import io.micrometer.observation.Observation;
import io.micrometer.observation.ObservationRegistry;
import io.nats.client.Message;
import io.nats.client.support.NatsJetStreamConstants;
import java.util.Optional;
import java.util.concurrent.atomic.AtomicBoolean;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

/**
 * The observation a consumer wraps around processing one NATS message: a CONSUMER span {@code process <subject>} and
 * the {@code nats.process} timer. The span <em>links</em> to the trace the event was recorded in ({@code traceparent},
 * {@code tracestate} headers) instead of continuing it, because delivery is at least once and may be hours late.
 *
 * <p>Telemetry never fails consuming: a message without trace headers is processed without a link, and a malformed
 * header is ignored (the first one is logged at WARN, later ones at DEBUG, so one broken producer cannot flood the
 * logs).
 */
final class NatsProcessObservations {

    /** Observation name; the timer is exported as {@code nats.process}. */
    static final String NAME = "nats.process";

    private static final Logger log = LoggerFactory.getLogger(NatsProcessObservations.class);

    private final ObservationRegistry registry;
    private final AtomicBoolean malformedHeaderReported = new AtomicBoolean();

    /**
     * Creates the observations.
     *
     * @param registry where they are recorded
     */
    NatsProcessObservations(ObservationRegistry registry) {
        this.registry = registry;
    }

    /**
     * Creates the observation for processing one message; the caller starts and stops it, for example with {@code
     * observation.observe(() -> handle(message))}.
     *
     * @param message the received message
     * @return the not yet started observation
     */
    Observation of(Message message) {
        var subject = message.getSubject();
        var creationContext = creationContextOf(message);
        var observation = Observation.createNotStarted(
                        NAME, () -> LinkedMessageContext.consumer(creationContext), registry)
                .contextualName("process " + subject)
                .lowCardinalityKeyValue(MessagingObservationKeys.MESSAGING_SYSTEM, MessagingObservationKeys.NATS)
                .lowCardinalityKeyValue(MessagingObservationKeys.DESTINATION, subject);
        var messageId = messageId(message);
        // A message published outside the relay may have no Nats-Msg-Id; a key value must not be null.
        return messageId == null
                ? observation
                : observation.highCardinalityKeyValue(MessagingObservationKeys.MESSAGE_ID, messageId);
    }

    private Optional<W3cTraceContext> creationContextOf(Message message) {
        var headers = message.getHeaders();
        if (headers == null || headers.getFirst(W3cTraceContext.TRACEPARENT) == null) {
            return Optional.empty();
        }
        var creationContext = W3cTraceContext.parse(
                headers.getFirst(W3cTraceContext.TRACEPARENT), headers.getFirst(W3cTraceContext.TRACESTATE));
        if (creationContext.isEmpty()) {
            reportMalformedHeader(message);
        }
        return creationContext;
    }

    private void reportMalformedHeader(Message message) {
        // One broken producer must not flood the logs: only the first occurrence is worth a human's attention.
        var entry = malformedHeaderReported.compareAndSet(false, true) ? log.atWarn() : log.atDebug();
        entry.addKeyValue(LogFields.SUBJECT, message.getSubject())
                .addKeyValue(LogFields.EVENT_ID, messageId(message))
                .log("Ignoring a malformed traceparent header; the message is processed without a link to its"
                        + " producing trace");
    }

    private static String messageId(Message message) {
        var headers = message.getHeaders();
        return headers == null ? null : headers.getFirst(NatsJetStreamConstants.MSG_ID_HDR);
    }
}
