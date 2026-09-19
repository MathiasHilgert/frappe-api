package com.frappe.platform.infrastructure.nats;

import static org.assertj.core.api.Assertions.assertThat;
import static org.awaitility.Awaitility.await;

import com.frappe.TestNatsConfiguration;
import com.frappe.TestTracingConfiguration;
import com.frappe.TestcontainersConfiguration;
import com.frappe.platform.DomainEvent;
import com.frappe.platform.DomainEventPublisher;
import com.frappe.platform.IdGenerator;
import com.frappe.platform.infrastructure.ids.TestIds;
import com.frappe.platform.infrastructure.tracing.W3cTraceContext;
import io.micrometer.observation.Observation;
import io.micrometer.observation.ObservationRegistry;
import io.micrometer.tracing.Tracer;
import io.nats.client.JetStreamApiException;
import io.nats.client.api.MessageInfo;
import io.opentelemetry.api.trace.SpanKind;
import io.opentelemetry.api.trace.StatusCode;
import io.opentelemetry.sdk.testing.exporter.InMemorySpanExporter;
import io.opentelemetry.sdk.trace.data.LinkData;
import io.opentelemetry.sdk.trace.data.SpanData;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.time.ZoneOffset;
import java.util.UUID;
import java.util.function.Supplier;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.micrometer.tracing.test.autoconfigure.AutoConfigureTracing;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.annotation.Import;
import org.springframework.modulith.events.EventPublication;
import org.springframework.modulith.events.Externalized;
import org.springframework.modulith.events.IncompleteEventPublications;
import org.springframework.modulith.events.core.EventPublicationRegistry;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.transaction.support.TransactionTemplate;
import org.testcontainers.DockerClientFactory;
import org.testcontainers.containers.GenericContainer;

/**
 * The trace context an event was recorded in travels with its NATS message, on the first publish and after a
 * resubmission, and the publish span links to it.
 */
@SpringBootTest(
        properties = {"frappe.nats.publish-timeout=1s", "management.opentelemetry.tracing.export.schedule-delay=50ms"})
@AutoConfigureTracing
@Import({TestcontainersConfiguration.class, TestNatsConfiguration.class, TestTracingConfiguration.class})
@ActiveProfiles("local")
class NatsTracePropagationTests {

    @Externalized
    record OrderPlaced(UUID eventId, Instant occurredAt, UUID aggregateId, long aggregateVersion, int eventVersion)
            implements DomainEvent {}

    @Externalized
    record OrderAmended(UUID eventId, Instant occurredAt, UUID aggregateId, long aggregateVersion, int eventVersion)
            implements DomainEvent {}

    @Externalized
    record OrderVoided(UUID eventId, Instant occurredAt, UUID aggregateId, long aggregateVersion, int eventVersion)
            implements DomainEvent {}

    @Externalized
    record OrderPicked(UUID eventId, Instant occurredAt, UUID aggregateId, long aggregateVersion, int eventVersion)
            implements DomainEvent {}

    @Externalized
    record OrderServed(UUID eventId, Instant occurredAt, UUID aggregateId, long aggregateVersion, int eventVersion)
            implements DomainEvent {}

    static final Duration WAIT = Duration.ofSeconds(10);

    final Clock clock = Clock.fixed(Instant.parse("2026-09-18T12:00:00Z"), ZoneOffset.UTC);

    final IdGenerator ids = TestIds.withClock(clock);

    @Autowired
    DomainEventPublisher publisher;

    @Autowired
    TransactionTemplate transactions;

    @Autowired
    ObservationRegistry observations;

    @Autowired
    Tracer tracer;

    @Autowired
    InMemorySpanExporter spans;

    @Autowired
    NatsClient client;

    @Autowired
    GenericContainer<?> natsContainer;

    @Autowired
    EventPublicationRegistry registry;

    @Autowired
    IncompleteEventPublications incomplete;

    @Autowired
    NatsProcessObservations processObservations;

    @BeforeEach
    void forgetEarlierSpans() {
        spans.reset();
    }

    @Test
    void carriesTheTraceContextTheEventWasRecordedIn() {
        // Given
        var event = new OrderPlaced(ids.newId(), clock.instant(), ids.newId(), 1, 1);

        // When
        var commandTrace = inTrace("test.command", () -> publish(event));

        // Then
        var message = awaitMessage("frappe.platform.order-placed.v1");
        assertThat(traceContextOf(message).traceId()).isEqualTo(commandTrace);
    }

    @Test
    void keepsTheOriginalTraceContextWhenResubmittedAfterAnOutage() {
        // Given the first publish fails while NATS is paused
        var event = new OrderAmended(ids.newId(), clock.instant(), ids.newId(), 1, 1);
        var commandTrace = withNatsPaused(() -> {
            var trace = inTrace("test.command", () -> publish(event));
            await().atMost(WAIT).until(() -> isFailed(event));
            return trace;
        });

        // When it is resubmitted in another trace
        var recoveryTrace = inTrace(
                "test.recovery",
                () -> incomplete.resubmitIncompletePublications(publication -> matches(publication, event)));

        // Then the message carries the original context, and the late publish links to it instead of joining it
        var subject = "frappe.platform.order-amended.v1";
        var message = awaitMessage(subject);
        assertThat(traceContextOf(message).traceId()).isEqualTo(commandTrace).isNotEqualTo(recoveryTrace);
        var resubmittedPublish =
                await().atMost(WAIT).until(() -> successfulSpan("publish " + subject), span -> span != null);
        assertThat(resubmittedPublish.getTraceId()).isNotEqualTo(commandTrace);
        assertThat(resubmittedPublish.getLinks())
                .extracting(link -> link.getSpanContext().getTraceId())
                .containsExactly(commandTrace);
    }

    @Test
    void publishesWithoutTraceHeadersWhenTheEventWasRecordedWithoutATrace() {
        // Given
        var event = new OrderVoided(ids.newId(), clock.instant(), ids.newId(), 1, 1);

        // When
        publish(event);

        // Then
        var headers = awaitMessage("frappe.platform.order-voided.v1").getHeaders();
        assertThat(headers.getFirst("Nats-Msg-Id")).isEqualTo(event.eventId().toString());
        assertThat(headers.containsKey("traceparent")).isFalse();
        assertThat(headers.containsKey("tracestate")).isFalse();
    }

    @Test
    void observesThePublishAsAProducerSpanLinkedToTheRecordedContext() {
        // Given
        var subject = "frappe.platform.order-served.v1";
        var event = new OrderServed(ids.newId(), clock.instant(), ids.newId(), 1, 1);

        // When
        inTrace("test.command", () -> publish(event));

        // Then
        var recorded = traceContextOf(awaitMessage(subject));
        var publishSpan = await().atMost(WAIT).until(() -> finishedSpan("publish " + subject), span -> span != null);
        assertThat(publishSpan.getKind()).isEqualTo(SpanKind.PRODUCER);
        assertThat(publishSpan.getLinks()).extracting(LinkData::getSpanContext).anySatisfy(linked -> {
            assertThat(linked.getTraceId()).isEqualTo(recorded.traceId());
            assertThat(linked.getSpanId()).isEqualTo(recorded.spanId());
        });
    }

    @Test
    void aConsumerSpanLinksToTheProducingSpanInsteadOfJoiningItsTrace() throws Exception {
        // Given an event published in a command trace
        var subject = "frappe.platform.order-picked.v1";
        var event = new OrderPicked(ids.newId(), clock.instant(), ids.newId(), 1, 1);
        var commandTrace = inTrace("test.command", () -> publish(event));
        awaitMessage(subject);

        // When a consumer processes the message
        var subscription = client.connection().jetStream().subscribe(subject);
        try {
            var message = subscription.nextMessage(WAIT);
            processObservations.of(message).observe(() -> {});
        } finally {
            subscription.unsubscribe();
        }

        // Then
        var processSpan = await().atMost(WAIT).until(() -> finishedSpan("process " + subject), span -> span != null);
        assertThat(processSpan.getKind()).isEqualTo(SpanKind.CONSUMER);
        assertThat(processSpan.getTraceId()).isNotEqualTo(commandTrace);
        assertThat(processSpan.getLinks())
                .extracting(link -> link.getSpanContext().getTraceId())
                .containsExactly(commandTrace);
    }

    private void publish(DomainEvent event) {
        transactions.executeWithoutResult(status -> publisher.publish(event));
    }

    // Stands in for the request or scheduled task trace around the action; returns its trace id.
    private String inTrace(String name, Runnable action) {
        return Observation.createNotStarted(name, observations).observe(() -> {
            action.run();
            return tracer.currentSpan().context().traceId();
        });
    }

    private <T> T withNatsPaused(Supplier<T> action) {
        var docker = DockerClientFactory.instance().client();
        var container = natsContainer.getContainerId();
        docker.pauseContainerCmd(container).exec();
        try {
            return action.get();
        } finally {
            docker.unpauseContainerCmd(container).exec();
        }
    }

    private MessageInfo awaitMessage(String subject) {
        return await().atMost(WAIT)
                .ignoreExceptionsInstanceOf(JetStreamApiException.class)
                .until(
                        () -> client.connection()
                                .jetStreamManagement()
                                .getLastMessage(NatsStreamProvisioner.STREAM, subject),
                        message -> message != null);
    }

    private static W3cTraceContext traceContextOf(MessageInfo message) {
        var headers = message.getHeaders();
        return W3cTraceContext.parse(headers.getFirst("traceparent"), headers.getFirst("tracestate"))
                .orElseThrow(() -> new AssertionError("no valid traceparent header in " + headers));
    }

    private SpanData finishedSpan(String name) {
        return spans.getFinishedSpanItems().stream()
                .filter(span -> span.getName().equals(name))
                .findFirst()
                .orElse(null);
    }

    private SpanData successfulSpan(String name) {
        return spans.getFinishedSpanItems().stream()
                .filter(span -> span.getName().equals(name))
                .filter(span -> span.getStatus().getStatusCode() != StatusCode.ERROR)
                .findFirst()
                .orElse(null);
    }

    private boolean isFailed(DomainEvent event) {
        return registry.findIncompletePublications().stream()
                .anyMatch(it -> matches(it, event) && it.getStatus() == EventPublication.Status.FAILED);
    }

    private static boolean matches(EventPublication publication, DomainEvent event) {
        return publication.getEvent() instanceof DomainEvent e && e.eventId().equals(event.eventId());
    }
}
