package com.frappe.platform.infrastructure.nats;

import static org.assertj.core.api.Assertions.assertThat;

import ch.qos.logback.classic.Level;
import ch.qos.logback.classic.Logger;
import ch.qos.logback.classic.spi.ILoggingEvent;
import ch.qos.logback.core.read.ListAppender;
import com.frappe.platform.infrastructure.tracing.LinkedMessageContext;
import com.frappe.platform.infrastructure.tracing.W3cTraceContext;
import io.micrometer.observation.tck.TestObservationRegistry;
import io.micrometer.observation.tck.TestObservationRegistryAssert;
import io.micrometer.observation.transport.Kind;
import io.nats.client.Message;
import io.nats.client.impl.Headers;
import io.nats.client.impl.NatsMessage;
import java.util.Arrays;
import java.util.List;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.slf4j.LoggerFactory;

class NatsProcessObservationsTest {

    private static final String SUBJECT = "frappe.platform.seat-freed.v1";
    private static final String EVENT_ID = "01923f5e-0000-7000-8000-000000000001";
    private static final String TRACEPARENT = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
    private static final String TRACESTATE = "rojo=00f067aa0ba902b7";

    private final TestObservationRegistry observations = TestObservationRegistry.create();

    private final NatsProcessObservations processObservations = new NatsProcessObservations(observations);

    private final Logger logger = (Logger) LoggerFactory.getLogger(NatsProcessObservations.class);

    private final ListAppender<ILoggingEvent> logs = new ListAppender<>();

    private Level originalLevel;

    @BeforeEach
    void captureLogs() {
        originalLevel = logger.getLevel();
        logger.setLevel(Level.DEBUG);
        logs.start();
        logger.addAppender(logs);
    }

    @AfterEach
    void releaseLogs() {
        logger.detachAppender(logs);
        logger.setLevel(originalLevel);
    }

    @Test
    void observesProcessingAsAConsumerLinkedToTheCreationContextOfTheMessage() {
        // Given
        var message = message(new Headers()
                .put("Nats-Msg-Id", EVENT_ID)
                .put("traceparent", TRACEPARENT)
                .put("tracestate", TRACESTATE));

        // When
        processObservations.of(message).observe(() -> {});

        // Then
        TestObservationRegistryAssert.assertThat(observations)
                .hasSingleObservationThat()
                .hasNameEqualTo("nats.process")
                .hasContextualNameEqualTo("process " + SUBJECT)
                .hasLowCardinalityKeyValue("messaging.system", "nats")
                .hasLowCardinalityKeyValue("messaging.destination.name", SUBJECT)
                .hasHighCardinalityKeyValue("messaging.message.id", EVENT_ID)
                .hasBeenStopped()
                .isInstanceOfSatisfying(LinkedMessageContext.class, context -> {
                    assertThat(context.getKind()).isEqualTo(Kind.CONSUMER);
                    assertThat(context.getCreationContext()).contains(new W3cTraceContext(TRACEPARENT, TRACESTATE));
                });
        assertThat(logs.list).isEmpty();
    }

    @Test
    void processesAMessageWithoutTraceHeadersWithoutALinkOrALogLine() {
        // Given a message without any headers and one with headers but no trace context
        var bare = message(null);
        var untraced = message(new Headers().put("Nats-Msg-Id", EVENT_ID));

        // When
        var observed = observe(bare, untraced);

        // Then
        assertThat(observed)
                .allSatisfy(context -> assertThat(context.getCreationContext()).isEmpty());
        assertThat(logs.list).isEmpty();
    }

    @Test
    void processesAMessageWithAMalformedTraceparentWithoutALinkAndWarnsOnlyOnce() {
        // Given
        var malformed = message(new Headers().put("Nats-Msg-Id", EVENT_ID).put("traceparent", "garbage"));

        // When
        var observed = observe(malformed, malformed);

        // Then
        assertThat(observed)
                .allSatisfy(context -> assertThat(context.getCreationContext()).isEmpty());
        assertThat(logs.list).extracting(ILoggingEvent::getLevel).containsExactly(Level.WARN, Level.DEBUG);
        assertThat(logs.list.getFirst().getKeyValuePairs())
                .anySatisfy(pair -> {
                    assertThat(pair.key).isEqualTo("nats.subject");
                    assertThat(pair.value).isEqualTo(SUBJECT);
                })
                .anySatisfy(pair -> {
                    assertThat(pair.key).isEqualTo("frappe.event_id");
                    assertThat(pair.value).isEqualTo(EVENT_ID);
                });
    }

    private List<LinkedMessageContext> observe(Message... messages) {
        return Arrays.stream(messages)
                .map(message -> {
                    var observation = processObservations.of(message);
                    observation.observe(() -> {});
                    return (LinkedMessageContext) observation.getContext();
                })
                .toList();
    }

    private static Message message(Headers headers) {
        return NatsMessage.builder()
                .subject(SUBJECT)
                .headers(headers)
                .data(new byte[0])
                .build();
    }
}
