package com.frappe.platform.infrastructure.nats;

import static org.assertj.core.api.Assertions.assertThat;

import com.frappe.platform.DomainEvent;
import java.time.Duration;
import java.time.Instant;
import java.util.UUID;
import java.util.concurrent.CompletionException;
import org.junit.jupiter.api.Test;
import org.springframework.modulith.events.RoutingTarget;
import tools.jackson.databind.json.JsonMapper;

class NatsEventTransportTest {

    private static final String SUBJECT = "frappe.platform.seat-freed.v1";

    record SeatFreed(UUID eventId, Instant occurredAt, UUID aggregateId, long aggregateVersion, int eventVersion)
            implements DomainEvent {}

    @Test
    void failsThePublicationWithTheEventAndSubjectWhenNatsIsUnavailable() {
        // Given a client that never connected
        var client = new NatsClient(
                new NatsProperties(
                        "nats://localhost:1",
                        "test",
                        Duration.ofMillis(1),
                        Duration.ofSeconds(1),
                        Duration.ofSeconds(1)),
                connection -> {});
        var transport = new NatsEventTransport(
                client, Duration.ofSeconds(1), JsonMapper.builder().build());
        var event = new SeatFreed(UUID.randomUUID(), Instant.EPOCH, UUID.randomUUID(), 1, 1);

        // When the event is externalized
        var result =
                transport.externalize(event, RoutingTarget.forTarget(SUBJECT).withoutKey());

        // Then the future fails with an EventPublicationException naming event and subject, keeping the cause
        assertThat(result)
                .failsWithin(Duration.ofSeconds(1))
                .withThrowableThat()
                .havingCause()
                .isInstanceOf(EventPublicationException.class)
                .withMessageContaining(event.eventId().toString())
                .withMessageContaining(SUBJECT)
                .withCauseInstanceOf(NatsUnavailableException.class);
    }

    static Class<?> unwrap() {
        return CompletionException.class;
    }
}
