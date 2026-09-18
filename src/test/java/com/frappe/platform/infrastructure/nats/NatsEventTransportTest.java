package com.frappe.platform.infrastructure.nats;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.ArgumentMatchers.anyString;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.when;

import com.frappe.platform.DomainEvent;
import io.micrometer.observation.ObservationRegistry;
import io.micrometer.observation.tck.TestObservationRegistry;
import io.micrometer.observation.tck.TestObservationRegistryAssert;
import io.nats.client.Connection;
import io.nats.client.ConnectionListener.Events;
import io.nats.client.JetStream;
import io.nats.client.api.PublishAck;
import io.nats.client.impl.Headers;
import java.time.Duration;
import java.time.Instant;
import java.util.UUID;
import org.junit.jupiter.api.Test;
import org.springframework.modulith.events.RoutingTarget;
import tools.jackson.databind.json.JsonMapper;

class NatsEventTransportTest {

    private static final String SUBJECT = "frappe.platform.seat-freed.v1";
    private static final UUID EVENT_ID = UUID.fromString("01923f5e-0000-7000-8000-000000000001");
    private static final UUID AGGREGATE_ID = UUID.fromString("01923f5e-0000-7000-8000-000000000002");
    private static final Duration TIMEOUT = Duration.ofSeconds(1);

    record SeatFreed(UUID eventId, Instant occurredAt, UUID aggregateId, long aggregateVersion, int eventVersion)
            implements DomainEvent {}

    private final TestObservationRegistry observations = TestObservationRegistry.create();

    private final SeatFreed event = new SeatFreed(EVENT_ID, Instant.EPOCH, AGGREGATE_ID, 1, 1);

    @Test
    void failsThePublicationWithTheEventAndSubjectWhenNatsIsUnavailable() {
        // Given a client that never connected
        var transport = transport(client());

        // When the event is externalized
        var result =
                transport.externalize(event, RoutingTarget.forTarget(SUBJECT).withoutKey());

        // Then the future fails with an EventPublicationException naming event and subject, keeping the cause
        assertThat(result)
                .failsWithin(TIMEOUT)
                .withThrowableThat()
                .havingCause()
                .isInstanceOf(EventPublicationException.class)
                .withMessageContaining(EVENT_ID.toString())
                .withMessageContaining(SUBJECT)
                .withCauseInstanceOf(NatsUnavailableException.class);
    }

    @Test
    void failsThePublicationWhenTheConnectionIsClosing() throws Exception {
        // Given a connection that jnats reports as closed while publishing
        var jetStream = mock(JetStream.class);
        when(jetStream.publish(anyString(), any(Headers.class), any(byte[].class)))
                .thenThrow(new IllegalStateException("Connection is Closed"));
        var connection = mock(Connection.class);
        when(connection.jetStream(any())).thenReturn(jetStream);
        var client = client();
        client.onEvent(connection, Events.CONNECTED);

        // When the event is externalized
        var result = transport(client)
                .externalize(event, RoutingTarget.forTarget(SUBJECT).withoutKey());

        // Then the publication fails instead of throwing, as NATS being unavailable
        assertThat(result)
                .failsWithin(TIMEOUT)
                .withThrowableThat()
                .havingCause()
                .isInstanceOf(EventPublicationException.class)
                .withCauseInstanceOf(NatsUnavailableException.class);
    }

    @Test
    void observesASuccessfulPublishWithTheSubjectAsALowCardinalityKey() throws Exception {
        // Given a connected client whose JetStream acks the message
        var jetStream = mock(JetStream.class);
        when(jetStream.publish(anyString(), any(Headers.class), any(byte[].class)))
                .thenReturn(mock(PublishAck.class));
        var connection = mock(Connection.class);
        when(connection.jetStream(any())).thenReturn(jetStream);
        var client = client();
        client.onEvent(connection, Events.CONNECTED);

        // When
        transport(client, observations)
                .externalize(event, RoutingTarget.forTarget(SUBJECT).withoutKey());

        // Then
        TestObservationRegistryAssert.assertThat(observations)
                .hasSingleObservationThat()
                .hasNameEqualTo("nats.publish")
                .hasContextualNameEqualTo("publish " + SUBJECT)
                .hasLowCardinalityKeyValue("messaging.system", "nats")
                .hasLowCardinalityKeyValue("messaging.destination.name", SUBJECT)
                .hasHighCardinalityKeyValue("messaging.message.id", EVENT_ID.toString())
                .hasBeenStarted()
                .hasBeenStopped()
                .assertThatError()
                .isNull();
    }

    @Test
    void observesAFailedPublishWithItsError() {
        // Given a client that never connected
        var transport = transport(client(), observations);

        // When
        transport.externalize(event, RoutingTarget.forTarget(SUBJECT).withoutKey());

        // Then
        TestObservationRegistryAssert.assertThat(observations)
                .hasSingleObservationThat()
                .hasNameEqualTo("nats.publish")
                .hasBeenStopped()
                .assertThatError()
                .isInstanceOf(NatsUnavailableException.class);
    }

    private static NatsClient client() {
        return new NatsClient(
                new NatsProperties("nats://localhost:1", "test", Duration.ofMillis(1), TIMEOUT, TIMEOUT),
                connection -> {});
    }

    private static NatsEventTransport transport(NatsClient client) {
        return transport(client, ObservationRegistry.NOOP);
    }

    private static NatsEventTransport transport(NatsClient client, ObservationRegistry observations) {
        return new NatsEventTransport(client, TIMEOUT, JsonMapper.builder().build(), observations);
    }
}
