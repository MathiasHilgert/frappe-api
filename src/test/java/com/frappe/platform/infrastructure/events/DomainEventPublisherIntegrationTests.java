package com.frappe.platform.infrastructure.events;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import com.frappe.TestcontainersConfiguration;
import com.frappe.platform.DomainEvent;
import com.frappe.platform.DomainEventPublisher;
import com.frappe.platform.IdGenerator;
import com.frappe.platform.infrastructure.ids.TestIds;
import java.time.Clock;
import java.time.Instant;
import java.time.ZoneOffset;
import java.util.List;
import java.util.UUID;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.annotation.Import;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.modulith.events.Externalized;
import org.springframework.modulith.events.core.EventSerializer;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.context.DynamicPropertyRegistry;
import org.springframework.test.context.DynamicPropertySource;
import org.springframework.transaction.IllegalTransactionStateException;
import org.springframework.transaction.support.TransactionTemplate;
import tools.jackson.core.JacksonException;

/**
 * The outbox write itself. NATS points at a closed port, so the relay never completes a publication and every row
 * stays in {@code platform.event_publication} where the test can count it.
 */
@SpringBootTest(properties = {"frappe.nats.connection-timeout=200ms", "frappe.nats.publish-timeout=200ms"})
@Import(TestcontainersConfiguration.class)
@ActiveProfiles("local")
class DomainEventPublisherIntegrationTests {

    @DynamicPropertySource
    static void unreachableNats(DynamicPropertyRegistry registry) {
        registry.add("frappe.nats.url", () -> "nats://localhost:" + TestPorts.closedPort());
    }

    @Externalized
    record TableOpened(UUID eventId, Instant occurredAt, UUID aggregateId, long aggregateVersion, int eventVersion)
            implements DomainEvent {}

    final Clock clock = Clock.fixed(Instant.parse("2026-09-18T12:00:00Z"), ZoneOffset.UTC);

    final IdGenerator ids = TestIds.withClock(clock);

    @Autowired
    DomainEventPublisher publisher;

    @Autowired
    TransactionTemplate transactions;

    @Autowired
    JdbcTemplate jdbc;

    @Autowired
    EventSerializer serializer;

    @Test
    void committedTransactionStoresOnePublicationRowPerEvent() {
        // Given
        var first = tableOpened();
        var second = tableOpened();

        // When
        transactions.executeWithoutResult(status -> publisher.publishAll(List.of(first, second)));

        // Then
        assertThat(publicationRows(first)).isOne();
        assertThat(publicationRows(second)).isOne();
    }

    @Test
    void theFirstPublishCountsAsTheFirstAttempt() {
        // Given
        var event = tableOpened();

        // When
        transactions.executeWithoutResult(status -> publisher.publish(event));

        // Then
        // frappe.outbox.recovery.max-attempts counts total attempts on this basis (Modulith stores 1 on publish).
        assertThat(jdbc.queryForObject(
                        "select completion_attempts from platform.event_publication where serialized_event like ?",
                        Integer.class,
                        "%" + event.eventId() + "%"))
                .isOne();
    }

    @Test
    void theRegistrySerializerRejectsAnUnreadablePayloadWithAJacksonException() {
        // When / Then
        // PublicationRedelivery and NatsConnectSetup catch exactly this type; no wrapping in Modulith 2.1.1.
        assertThatThrownBy(() -> serializer.deserialize("{\"eventId\": ", TableOpened.class))
                .isInstanceOf(JacksonException.class);
    }

    @Test
    void rolledBackTransactionStoresNoPublicationRow() {
        // Given
        var event = tableOpened();

        // When
        transactions.executeWithoutResult(status -> {
            publisher.publish(event);
            status.setRollbackOnly();
        });

        // Then
        assertThat(publicationRows(event)).isZero();
    }

    @Test
    void publishingOutsideATransactionFailsFast() {
        // Given
        var event = tableOpened();

        // When / Then
        // Without a transaction the event would never reach the outbox, so the call is a bug, not a lost event.
        assertThatThrownBy(() -> publisher.publish(event)).isInstanceOf(IllegalTransactionStateException.class);
        assertThatThrownBy(() -> publisher.publishAll(List.of(event)))
                .isInstanceOf(IllegalTransactionStateException.class);
        assertThat(publicationRows(event)).isZero();
    }

    private TableOpened tableOpened() {
        return new TableOpened(ids.newId(), clock.instant(), ids.newId(), 1, 1);
    }

    private int publicationRows(DomainEvent event) {
        var rows = jdbc.queryForObject(
                "select count(*) from platform.event_publication where serialized_event like ?",
                Integer.class,
                "%" + event.eventId() + "%");
        return rows == null ? 0 : rows;
    }
}
