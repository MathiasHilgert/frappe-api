package com.frappe.platform.infrastructure.events;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatNoException;
import static org.assertj.core.api.Assertions.tuple;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.ArgumentMatchers.anyInt;
import static org.mockito.Mockito.doThrow;
import static org.mockito.Mockito.inOrder;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

import ch.qos.logback.classic.Level;
import ch.qos.logback.classic.Logger;
import ch.qos.logback.classic.spi.ILoggingEvent;
import ch.qos.logback.core.read.ListAppender;
import com.frappe.platform.infrastructure.events.OutboxObservations.Trigger;
import com.frappe.platform.infrastructure.events.PublicationRedelivery.Outcome;
import io.micrometer.core.instrument.simple.SimpleMeterRegistry;
import io.micrometer.observation.tck.TestObservationRegistry;
import io.micrometer.observation.tck.TestObservationRegistryAssert;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.time.ZoneOffset;
import java.util.List;
import java.util.Set;
import java.util.UUID;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.slf4j.LoggerFactory;
import org.slf4j.event.KeyValuePair;
import org.springframework.dao.DataAccessResourceFailureException;

class FailedPublicationResubmitterTest {

    static final Instant NOW = Instant.parse("2026-09-18T12:00:00Z");

    final OutboxRecoveryProperties properties =
            new OutboxRecoveryProperties(Duration.ofSeconds(30), 50, Duration.ofMinutes(5), 20, Duration.ofHours(1));

    final PublicationRedelivery redelivery = mock(PublicationRedelivery.class);

    final OutboxRecoveryRepository outbox = mock(OutboxRecoveryRepository.class);

    final DeadLetterMetrics metrics = new DeadLetterMetrics();

    final Clock clock = Clock.fixed(NOW, ZoneOffset.UTC);

    final TestObservationRegistry observations = TestObservationRegistry.create();

    final FailedPublicationResubmitter resubmitter =
            new FailedPublicationResubmitter(redelivery, outbox, metrics, properties, clock, observations);

    final Logger logger = (Logger) LoggerFactory.getLogger(FailedPublicationResubmitter.class);

    final ListAppender<ILoggingEvent> logs = new ListAppender<>();

    @BeforeEach
    void captureLogs() {
        logs.start();
        logger.addAppender(logs);
    }

    @AfterEach
    void releaseLogs() {
        logger.detachAppender(logs);
    }

    @Test
    void releasesStuckAttemptsAndDeadLettersBeforeSelectingRetries() {
        // When
        resubmitter.recover(Trigger.SCHEDULED);

        // Then
        var order = inOrder(outbox);
        order.verify(outbox).releaseStuckPublications(Instant.parse("2026-09-18T11:55:00Z"));
        order.verify(outbox).deadLetterExhausted(20, NOW);
        order.verify(outbox).findRetryable(NOW, 50, Duration.ofSeconds(30), Duration.ofHours(1));
    }

    @Test
    void redeliversExactlyTheLoadedBatch() {
        // Given
        var first = failedPublication(Probe.class.getName());
        var second = failedPublication(Probe.class.getName());
        when(outbox.findRetryable(any(), anyInt(), any(), any())).thenReturn(List.of(first, second));
        when(redelivery.redeliver(any(), any())).thenReturn(Outcome.RESUBMITTED);

        // When
        resubmitter.recover(Trigger.SCHEDULED);

        // Then
        verify(redelivery).redeliver(first, NOW);
        verify(redelivery).redeliver(second, NOW);
        verify(outbox, never()).deadLetterByIds(any(), any(), any());
    }

    @Test
    void publicationsStillInFlightReduceTheBatch() {
        // Given
        when(outbox.countInFlight()).thenReturn(45L);

        // When
        resubmitter.recover(Trigger.SCHEDULED);

        // Then
        verify(outbox).findRetryable(NOW, 5, Duration.ofSeconds(30), Duration.ofHours(1));
    }

    @Test
    void aFullWindowOfInFlightPublicationsSkipsTheRun() {
        // Given
        when(outbox.countInFlight()).thenReturn(50L);

        // When
        resubmitter.recover(Trigger.SCHEDULED);

        // Then
        verify(outbox, never()).findRetryable(any(), anyInt(), any(), any());
        verify(redelivery, never()).redeliver(any(), any());
    }

    @Test
    void everyDeadLetterIsLoggedOnceAtErrorWithItsContext() {
        // Given
        var id = UUID.randomUUID();
        when(outbox.deadLetterExhausted(20, NOW))
                .thenReturn(List.of(new DeadLetter(
                        id, "com.frappe.Probe", "nats.listener", 20, DeadLetterReason.MAX_ATTEMPTS_EXHAUSTED)));

        // When
        resubmitter.recover(Trigger.SCHEDULED);

        // Then
        assertThat(logs.list).singleElement().satisfies(event -> {
            assertThat(event.getLevel()).isEqualTo(Level.ERROR);
            assertThat(event.getKeyValuePairs())
                    .extracting(pair -> pair.key, pair -> String.valueOf(pair.value))
                    .contains(
                            tuple("frappe.outbox.publication_id", id.toString()),
                            tuple("frappe.outbox.event_type", "com.frappe.Probe"),
                            tuple("frappe.outbox.listener_id", "nats.listener"),
                            tuple("frappe.outbox.completion_attempts", "20"),
                            tuple("frappe.outbox.dead_letter_reason", "MAX_ATTEMPTS_EXHAUSTED"));
        });
    }

    @Test
    void publicationsThatCanNeverBeDeliveredAreDeadLetteredWithTheirReason() {
        // Given
        var unreadable = failedPublication(Probe.class.getName());
        var removedType = failedPublication("com.frappe.removed.TableMerged");
        var orphaned = failedPublication(Probe.class.getName());
        var readable = failedPublication(Probe.class.getName());
        when(outbox.findRetryable(any(), anyInt(), any(), any()))
                .thenReturn(List.of(unreadable, removedType, orphaned, readable));
        when(redelivery.redeliver(unreadable, NOW)).thenReturn(Outcome.UNREADABLE_PAYLOAD);
        when(redelivery.redeliver(removedType, NOW)).thenReturn(Outcome.UNKNOWN_EVENT_TYPE);
        when(redelivery.redeliver(orphaned, NOW)).thenReturn(Outcome.UNKNOWN_LISTENER);
        when(redelivery.redeliver(readable, NOW)).thenReturn(Outcome.RESUBMITTED);

        // When
        resubmitter.recover(Trigger.SCHEDULED);

        // Then
        verify(redelivery).redeliver(readable, NOW);
        verify(outbox).deadLetterByIds(Set.of(unreadable.id()), DeadLetterReason.UNREADABLE_PAYLOAD, NOW);
        verify(outbox).deadLetterByIds(Set.of(removedType.id()), DeadLetterReason.UNKNOWN_EVENT_TYPE, NOW);
        verify(outbox).deadLetterByIds(Set.of(orphaned.id()), DeadLetterReason.UNKNOWN_LISTENER, NOW);
    }

    @Test
    void theDeadLetterGaugeReportsTheStoredCount() {
        // Given
        var registry = new SimpleMeterRegistry();
        metrics.bindTo(registry);
        when(outbox.countDeadLetters()).thenReturn(3L);

        // When
        resubmitter.recover(Trigger.SCHEDULED);

        // Then
        assertThat(registry.get("outbox.dead.letters").gauge().value()).isEqualTo(3.0);
    }

    @Test
    void aPassTriggeredByARecoveredTransportIgnoresTheBackoffButKeepsTheBatch() {
        // When
        resubmitter.recover(Trigger.TRANSPORT_RECOVERED);

        // Then
        // Failures caused by the outage are due at once; the batch still bounds the run.
        verify(outbox).findRetryable(NOW, 50, Duration.ZERO, Duration.ofHours(1));
    }

    @Test
    void everyPassIsObservedWithItsTrigger() {
        // When
        resubmitter.recover(Trigger.SCHEDULED);
        resubmitter.recover(Trigger.TRANSPORT_RECOVERED);

        // Then
        TestObservationRegistryAssert.assertThat(observations)
                .hasNumberOfObservationsWithNameEqualTo("outbox.recovery", 2)
                .hasAnObservation(
                        observation -> observation.hasLowCardinalityKeyValue("outbox.recovery.trigger", "scheduled"))
                .hasAnObservation(observation ->
                        observation.hasLowCardinalityKeyValue("outbox.recovery.trigger", "transport_recovered"));
    }

    @Test
    void everyRedeliveryIsObservedWithItsOutcomeAndPublicationId() {
        // Given
        var publication = failedPublication(Probe.class.getName());
        when(outbox.findRetryable(any(), anyInt(), any(), any())).thenReturn(List.of(publication));
        when(redelivery.redeliver(publication, NOW)).thenReturn(Outcome.UNREADABLE_PAYLOAD);

        // When
        resubmitter.recover(Trigger.SCHEDULED);

        // Then
        TestObservationRegistryAssert.assertThat(observations)
                .hasAnObservation(observation -> observation
                        .hasNameEqualTo("outbox.redelivery")
                        .hasLowCardinalityKeyValue("outbox.redelivery.outcome", "unreadable_payload")
                        .hasHighCardinalityKeyValue(
                                "outbox.publication.id", publication.id().toString()));
    }

    @Test
    void aThrowingRedeliveryIsObservedAsAnErroredRedelivery() {
        // Given
        var publication = failedPublication(Probe.class.getName());
        var outage = new DataAccessResourceFailureException("connection refused");
        when(outbox.findRetryable(any(), anyInt(), any(), any())).thenReturn(List.of(publication));
        when(redelivery.redeliver(publication, NOW)).thenThrow(outage);

        // When
        resubmitter.recover(Trigger.SCHEDULED);

        // Then
        TestObservationRegistryAssert.assertThat(observations)
                .hasAnObservation(observation -> observation
                        .hasNameEqualTo("outbox.redelivery")
                        .hasLowCardinalityKeyValue("outbox.redelivery.outcome", "error")
                        .hasError(outage));
    }

    @Test
    void aFailedPassIsObservedAsAnError() {
        // Given
        doThrow(new DataAccessResourceFailureException("connection refused"))
                .when(outbox)
                .releaseStuckPublications(any());

        // When
        resubmitter.recover(Trigger.SCHEDULED);

        // Then
        TestObservationRegistryAssert.assertThat(observations)
                .hasSingleObservationThat()
                .hasNameEqualTo("outbox.recovery")
                .hasError()
                .hasBeenStopped();
    }

    @Test
    void databaseFailureIsLoggedOnceAndRetriedOnTheNextRun() {
        // Given
        var outage = new DataAccessResourceFailureException("connection refused");
        doThrow(outage).when(outbox).releaseStuckPublications(any());

        // When / Then
        assertThatNoException().isThrownBy(() -> resubmitter.recover(Trigger.SCHEDULED));
        assertThat(logs.list).singleElement().satisfies(event -> {
            assertThat(event.getLevel()).isEqualTo(Level.WARN);
            assertThat(event.getThrowableProxy().getMessage()).isEqualTo("connection refused");
            assertThat(event.getKeyValuePairs())
                    .extracting(KeyValuePair::toString)
                    .anySatisfy(pair -> assertThat(pair).startsWith("frappe.outbox.recovery_interval"))
                    .anySatisfy(pair -> assertThat(pair).startsWith("frappe.outbox.batch_size"));
        });
    }

    record Probe(UUID eventId) {}

    private static FailedPublication failedPublication(String eventType) {
        return new FailedPublication(UUID.randomUUID(), "nats.listener", eventType, "{}");
    }
}
