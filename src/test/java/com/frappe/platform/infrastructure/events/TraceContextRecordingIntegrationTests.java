package com.frappe.platform.infrastructure.events;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatIllegalStateException;

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
import java.time.Clock;
import java.time.Instant;
import java.time.ZoneOffset;
import java.util.Optional;
import java.util.UUID;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.micrometer.tracing.test.autoconfigure.AutoConfigureTracing;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.annotation.Import;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.modulith.events.Externalized;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.transaction.support.TransactionTemplate;

/**
 * The creation context of an event: the trace active when the event is recorded is stored with it, in the same
 * transaction as the outbox row.
 */
@SpringBootTest(
        properties = {"frappe.nats.publish-timeout=1s", "management.opentelemetry.tracing.export.schedule-delay=50ms"})
@AutoConfigureTracing
@Import({TestcontainersConfiguration.class, TestNatsConfiguration.class, TestTracingConfiguration.class})
@ActiveProfiles("local")
class TraceContextRecordingIntegrationTests {

    @Externalized
    record BillRequested(UUID eventId, Instant occurredAt, UUID aggregateId, long aggregateVersion, int eventVersion)
            implements DomainEvent {}

    record BillPrinted(UUID eventId, Instant occurredAt, UUID aggregateId, long aggregateVersion, int eventVersion)
            implements DomainEvent {}

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
    JdbcClient jdbc;

    @Test
    void recordsTheActiveTraceContextWithAnExternalizedEvent() {
        // Given
        var event = new BillRequested(ids.newId(), clock.instant(), ids.newId(), 1, 1);

        // When
        var traceId = inCommandTrace(() -> transactions.executeWithoutResult(status -> publisher.publish(event)));

        // Then
        assertThat(recordedTraceparent(event).flatMap(traceparent -> W3cTraceContext.parse(traceparent, null)))
                .hasValueSatisfying(context -> {
                    assertThat(context.traceId()).isEqualTo(traceId);
                    assertThat(context.sampled()).isTrue();
                });
    }

    @Test
    void recordsNothingWithoutAnActiveTrace() {
        // Given
        var event = new BillRequested(ids.newId(), clock.instant(), ids.newId(), 1, 1);

        // When
        transactions.executeWithoutResult(status -> publisher.publish(event));

        // Then
        assertThat(recordedTraceparent(event)).isEmpty();
    }

    @Test
    void rollsTheTraceContextBackWithTheEvent() {
        // Given
        var event = new BillRequested(ids.newId(), clock.instant(), ids.newId(), 1, 1);

        // When
        assertThatIllegalStateException()
                .isThrownBy(() -> inCommandTrace(() -> transactions.executeWithoutResult(status -> {
                    publisher.publish(event);
                    throw new IllegalStateException("command failed after recording its event");
                })));

        // Then
        assertThat(recordedTraceparent(event)).isEmpty();
    }

    @Test
    void recordsNothingForAnEventThatStaysInTheProcess() {
        // Given
        var event = new BillPrinted(ids.newId(), clock.instant(), ids.newId(), 1, 1);

        // When
        inCommandTrace(() -> transactions.executeWithoutResult(status -> publisher.publish(event)));

        // Then
        assertThat(recordedTraceparent(event)).isEmpty();
    }

    // Stands in for the HTTP request or command handler trace that records the event; returns its trace id.
    private String inCommandTrace(Runnable command) {
        return Observation.createNotStarted("test.command", observations).observe(() -> {
            command.run();
            return tracer.currentSpan().context().traceId();
        });
    }

    private Optional<String> recordedTraceparent(DomainEvent event) {
        return jdbc.sql("select traceparent from platform.event_trace_context where event_id = ?")
                .param(event.eventId())
                .query(String.class)
                .optional();
    }
}
