package com.frappe.platform.infrastructure.events;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.Mockito.doThrow;
import static org.mockito.Mockito.inOrder;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

import com.frappe.platform.infrastructure.events.PublicationRedelivery.Outcome;
import java.time.Instant;
import java.util.List;
import java.util.UUID;
import org.junit.jupiter.api.Test;
import org.mockito.ArgumentCaptor;
import org.springframework.context.ApplicationEvent;
import org.springframework.context.PayloadApplicationEvent;
import org.springframework.dao.DataAccessResourceFailureException;
import org.springframework.modulith.events.core.EventPublicationRepository;
import org.springframework.modulith.events.core.EventSerializer;
import org.springframework.transaction.event.TransactionalApplicationListener;
import tools.jackson.core.exc.StreamReadException;

class PublicationRedeliveryTest {

    static final Instant NOW = Instant.parse("2026-09-18T12:00:00Z");

    static final String LISTENER_ID = "nats.listener";

    record Probe(UUID eventId) {}

    final EventPublicationRepository repository = mock(EventPublicationRepository.class);

    final EventSerializer serializer = mock(EventSerializer.class);

    @SuppressWarnings("unchecked")
    final TransactionalApplicationListener<ApplicationEvent> listener = mock(TransactionalApplicationListener.class);

    final PublicationRedelivery redelivery = new PublicationRedelivery(
            repository, serializer, () -> List.of(listener), getClass().getClassLoader());

    final FailedPublication publication =
            new FailedPublication(UUID.randomUUID(), LISTENER_ID, Probe.class.getName(), "{}");

    @Test
    void claimsThePublicationThenHandsTheEventToItsListener() {
        // Given
        var event = new Probe(UUID.randomUUID());
        when(listener.getListenerId()).thenReturn(LISTENER_ID);
        when(serializer.deserialize("{}", Probe.class)).thenReturn(event);
        when(repository.markResubmitted(publication.id(), NOW)).thenReturn(true);
        var delivered = ArgumentCaptor.forClass(ApplicationEvent.class);

        // When
        var outcome = redelivery.redeliver(publication, NOW);

        // Then
        assertThat(outcome).isEqualTo(Outcome.RESUBMITTED);
        var order = inOrder(repository, listener);
        order.verify(repository).markResubmitted(publication.id(), NOW);
        order.verify(listener).processEvent(delivered.capture());
        assertThat(delivered.getValue())
                .isInstanceOfSatisfying(
                        PayloadApplicationEvent.class,
                        it -> assertThat(it.getPayload()).isEqualTo(event));
    }

    @Test
    void aPublicationClaimedByAnotherInstanceIsNotDeliveredAgain() {
        // Given
        when(listener.getListenerId()).thenReturn(LISTENER_ID);
        when(serializer.deserialize("{}", Probe.class)).thenReturn(new Probe(UUID.randomUUID()));
        when(repository.markResubmitted(publication.id(), NOW)).thenReturn(false);

        // When
        var outcome = redelivery.redeliver(publication, NOW);

        // Then
        assertThat(outcome).isEqualTo(Outcome.CLAIMED_ELSEWHERE);
        verify(listener, never()).processEvent(any());
    }

    @Test
    void anUnreadablePayloadIsReportedWithoutClaimingThePublication() {
        // Given
        when(listener.getListenerId()).thenReturn(LISTENER_ID);
        when(serializer.deserialize("{}", Probe.class))
                .thenThrow(new StreamReadException(null, "Unexpected end-of-input"));

        // When
        var outcome = redelivery.redeliver(publication, NOW);

        // Then
        assertThat(outcome).isEqualTo(Outcome.UNREADABLE_PAYLOAD);
        verify(repository, never()).markResubmitted(any(), any());
    }

    @Test
    void anEventTypeMissingFromTheClasspathIsReportedWithoutClaimingThePublication() {
        // Given
        var removed = new FailedPublication(UUID.randomUUID(), LISTENER_ID, "com.frappe.removed.TableMerged", "{}");

        // When
        var outcome = redelivery.redeliver(removed, NOW);

        // Then
        assertThat(outcome).isEqualTo(Outcome.UNKNOWN_EVENT_TYPE);
        verify(repository, never()).markResubmitted(any(), any());
    }

    @Test
    void aListenerThatNoLongerExistsIsReportedWithoutClaimingThePublication() {
        // Given
        when(listener.getListenerId()).thenReturn("another.listener");
        when(serializer.deserialize("{}", Probe.class)).thenReturn(new Probe(UUID.randomUUID()));

        // When
        var outcome = redelivery.redeliver(publication, NOW);

        // Then
        assertThat(outcome).isEqualTo(Outcome.UNKNOWN_LISTENER);
        verify(repository, never()).markResubmitted(any(), any());
    }

    @Test
    void aFailureToMarkTheFailedDeliveryIsReportedWithoutAbortingTheBatch() {
        // Given
        when(listener.getListenerId()).thenReturn(LISTENER_ID);
        when(serializer.deserialize("{}", Probe.class)).thenReturn(new Probe(UUID.randomUUID()));
        when(repository.markResubmitted(publication.id(), NOW)).thenReturn(true);
        doThrow(new IllegalStateException("listener bug")).when(listener).processEvent(any());
        doThrow(new DataAccessResourceFailureException("connection refused"))
                .when(repository)
                .markFailed(publication.id());

        // When
        var outcome = redelivery.redeliver(publication, NOW);

        // Then
        // The row stays RESUBMITTED; stuck-after releases it for the next retry.
        assertThat(outcome).isEqualTo(Outcome.LISTENER_FAILED);
    }

    @Test
    void aListenerFailingSynchronouslyLeavesThePublicationFailed() {
        // Given
        when(listener.getListenerId()).thenReturn(LISTENER_ID);
        when(serializer.deserialize("{}", Probe.class)).thenReturn(new Probe(UUID.randomUUID()));
        when(repository.markResubmitted(publication.id(), NOW)).thenReturn(true);
        doThrow(new IllegalStateException("listener bug")).when(listener).processEvent(any());

        // When
        var outcome = redelivery.redeliver(publication, NOW);

        // Then
        assertThat(outcome).isEqualTo(Outcome.LISTENER_FAILED);
        verify(repository).markFailed(publication.id());
    }
}
