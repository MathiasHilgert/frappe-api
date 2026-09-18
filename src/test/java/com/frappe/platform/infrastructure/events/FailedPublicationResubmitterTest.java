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
import io.micrometer.core.instrument.simple.SimpleMeterRegistry;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.time.ZoneOffset;
import java.util.List;
import java.util.UUID;
import java.util.function.Predicate;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.mockito.ArgumentCaptor;
import org.slf4j.LoggerFactory;
import org.slf4j.event.KeyValuePair;
import org.springframework.dao.DataAccessResourceFailureException;
import org.springframework.modulith.events.EventPublication;
import org.springframework.modulith.events.IncompleteEventPublications;

class FailedPublicationResubmitterTest {

    static final Instant NOW = Instant.parse("2026-09-18T12:00:00Z");

    final OutboxRecoveryProperties properties =
            new OutboxRecoveryProperties(Duration.ofSeconds(30), 50, Duration.ofMinutes(5), 20, Duration.ofHours(1));

    final IncompleteEventPublications incomplete = mock(IncompleteEventPublications.class);

    final OutboxRecoveryRepository outbox = mock(OutboxRecoveryRepository.class);

    final DeadLetterMetrics metrics = new DeadLetterMetrics();

    final Clock clock = Clock.fixed(NOW, ZoneOffset.UTC);

    final FailedPublicationResubmitter resubmitter =
            new FailedPublicationResubmitter(incomplete, outbox, metrics, properties, clock);

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
        resubmitter.run();

        // Then
        var order = inOrder(outbox);
        order.verify(outbox).releaseStuckPublications(Instant.parse("2026-09-18T11:55:00Z"));
        order.verify(outbox).deadLetterExhausted(20, NOW);
        order.verify(outbox).findRetryable(NOW, 50, Duration.ofSeconds(30), Duration.ofHours(1));
    }

    @Test
    void resubmitsOnlyTheSelectedPublications() {
        // Given
        var selected = UUID.randomUUID();
        when(outbox.findRetryable(any(), anyInt(), any(), any())).thenReturn(List.of(selected));
        @SuppressWarnings("unchecked")
        ArgumentCaptor<Predicate<EventPublication>> filter = ArgumentCaptor.forClass(Predicate.class);

        // When
        resubmitter.run();

        // Then
        verify(incomplete).resubmitIncompletePublications(filter.capture());
        assertThat(filter.getValue()).accepts(publication(selected)).rejects(publication(UUID.randomUUID()));
    }

    @Test
    void publicationsStillInFlightReduceTheBatch() {
        // Given
        when(outbox.countInFlight()).thenReturn(45L);

        // When
        resubmitter.run();

        // Then
        verify(outbox).findRetryable(NOW, 5, Duration.ofSeconds(30), Duration.ofHours(1));
    }

    @Test
    void aFullWindowOfInFlightPublicationsSkipsTheRun() {
        // Given
        when(outbox.countInFlight()).thenReturn(50L);

        // When
        resubmitter.run();

        // Then
        verify(outbox, never()).findRetryable(any(), anyInt(), any(), any());
        verify(incomplete, never()).resubmitIncompletePublications(any(Predicate.class));
    }

    @Test
    void everyDeadLetterIsLoggedOnceAtErrorWithItsContext() {
        // Given
        var id = UUID.randomUUID();
        when(outbox.deadLetterExhausted(20, NOW))
                .thenReturn(List.of(new DeadLetter(
                        id, "com.frappe.Probe", "nats.listener", 20, DeadLetterReason.MAX_ATTEMPTS_EXHAUSTED)));

        // When
        resubmitter.run();

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
    void theDeadLetterGaugeReportsTheStoredCount() {
        // Given
        var registry = new SimpleMeterRegistry();
        metrics.bindTo(registry);
        when(outbox.countDeadLetters()).thenReturn(3L);

        // When
        resubmitter.run();

        // Then
        assertThat(registry.get("frappe.outbox.dead.letters").gauge().value()).isEqualTo(3.0);
    }

    @Test
    void databaseFailureIsLoggedOnceAndRetriedOnTheNextRun() {
        // Given
        var outage = new DataAccessResourceFailureException("connection refused");
        doThrow(outage).when(outbox).releaseStuckPublications(any());

        // When / Then
        assertThatNoException().isThrownBy(resubmitter::run);
        assertThat(logs.list).singleElement().satisfies(event -> {
            assertThat(event.getLevel()).isEqualTo(Level.WARN);
            assertThat(event.getThrowableProxy().getMessage()).isEqualTo("connection refused");
            assertThat(event.getKeyValuePairs())
                    .extracting(KeyValuePair::toString)
                    .anySatisfy(pair -> assertThat(pair).startsWith("frappe.outbox.recovery_interval"))
                    .anySatisfy(pair -> assertThat(pair).startsWith("frappe.outbox.batch_size"));
        });
    }

    private static EventPublication publication(UUID id) {
        var publication = mock(EventPublication.class);
        when(publication.getIdentifier()).thenReturn(id);
        return publication;
    }
}
