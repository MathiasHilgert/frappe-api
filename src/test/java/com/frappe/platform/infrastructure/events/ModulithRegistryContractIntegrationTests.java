package com.frappe.platform.infrastructure.events;

import static org.assertj.core.api.Assertions.assertThat;
import static org.awaitility.Awaitility.await;

import com.frappe.TestcontainersConfiguration;
import com.frappe.platform.DomainEvent;
import com.frappe.platform.DomainEventPublisher;
import com.frappe.platform.IdGenerator;
import com.frappe.platform.infrastructure.ids.TestIds;
import java.time.Clock;
import java.time.Instant;
import java.time.ZoneOffset;
import java.time.temporal.ChronoUnit;
import java.util.UUID;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.annotation.Import;
import org.springframework.context.support.AbstractApplicationContext;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.modulith.events.Externalized;
import org.springframework.modulith.events.core.EventPublicationRegistry;
import org.springframework.modulith.events.core.EventPublicationRepository;
import org.springframework.modulith.events.core.PublicationTargetIdentifier;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.context.DynamicPropertyRegistry;
import org.springframework.test.context.DynamicPropertySource;
import org.springframework.transaction.event.TransactionalApplicationListener;
import org.springframework.transaction.support.TransactionTemplate;

/**
 * The Spring Modulith 2.1.1 behaviors {@link PublicationRedelivery} mirrors. If an upgrade changes one of them, this
 * class fails and the redelivery must be revisited before the upgrade ships. NATS is unreachable, so publications stay
 * in the outbox for inspection.
 */
@SpringBootTest
@Import(TestcontainersConfiguration.class)
@ActiveProfiles("local")
class ModulithRegistryContractIntegrationTests {

    @DynamicPropertySource
    static void unreachableNats(DynamicPropertyRegistry registry) {
        registry.add("frappe.nats.url", () -> "nats://localhost:" + TestPorts.closedPort());
    }

    @Externalized
    record CheckRequested(UUID eventId, Instant occurredAt, UUID aggregateId, long aggregateVersion, int eventVersion)
            implements DomainEvent {}

    final Clock clock = Clock.fixed(Instant.parse("2026-09-18T12:00:00Z"), ZoneOffset.UTC);

    final IdGenerator ids = TestIds.withClock(clock);

    @Autowired
    DomainEventPublisher publisher;

    @Autowired
    TransactionTemplate transactions;

    @Autowired
    JdbcClient jdbc;

    @Autowired
    AbstractApplicationContext context;

    @Autowired
    EventPublicationRepository repository;

    @Autowired
    EventPublicationRegistry registry;

    @Test
    void theStoredListenerIdNamesAListenerTheContextExposes() {
        // Given
        var event = published();

        // When
        var listenerId = column(event, "listener_id");

        // Then
        assertThat(context.getApplicationListeners())
                .filteredOn(TransactionalApplicationListener.class::isInstance)
                .map(listener -> ((TransactionalApplicationListener<?>) listener).getListenerId())
                .contains(listenerId);
    }

    @Test
    void markResubmittedClaimsOnceAndCountsTheAttempt() {
        // Given
        var event = published();
        var id = UUID.fromString(column(event, "id"));
        var at = Instant.now().truncatedTo(ChronoUnit.MILLIS);

        // When
        var first = repository.markResubmitted(id, at);
        var second = repository.markResubmitted(id, at.plusSeconds(1));

        // Then
        assertThat(first).isTrue();
        assertThat(second).isFalse();
        assertThat(column(event, "status")).isEqualTo("RESUBMITTED");
        assertThat(column(event, "completion_attempts")).isEqualTo("2");
        assertThat(jdbc.sql("select last_resubmission_date from platform.event_publication where id = ?")
                        .param(id)
                        .query(Instant.class)
                        .single())
                .isEqualTo(at);
    }

    @Test
    void completingByEventAndListenerWithoutInProgressStateArchivesThePublication() {
        // Given
        var event = published();
        var listenerId = column(event, "listener_id");

        // When
        registry.markCompleted(event, PublicationTargetIdentifier.of(listenerId));

        // Then
        assertThat(jdbc.sql("select count(*) from platform.event_publication where serialized_event like ?")
                        .param(pattern(event))
                        .query(Long.class)
                        .single())
                .isZero();
        assertThat(jdbc.sql("select status from platform.event_publication_archive where serialized_event like ?")
                        .param(pattern(event))
                        .query(String.class)
                        .single())
                .isEqualTo("COMPLETED");
    }

    private CheckRequested published() {
        var event = new CheckRequested(ids.newId(), clock.instant(), ids.newId(), 1, 1);
        transactions.executeWithoutResult(status -> publisher.publish(event));
        // The first attempt fails at once (NATS unreachable); wait so it cannot race the transitions under test.
        await().until(() -> "FAILED".equals(column(event, "status")));
        return event;
    }

    private String column(DomainEvent event, String column) {
        return jdbc.sql("select " + column + "::text from platform.event_publication where serialized_event like ?")
                .param(pattern(event))
                .query(String.class)
                .single();
    }

    private static String pattern(DomainEvent event) {
        return "%" + event.eventId() + "%";
    }
}
