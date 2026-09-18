package com.frappe.platform.infrastructure.events;

import static org.assertj.core.api.Assertions.assertThat;

import com.frappe.TestcontainersConfiguration;
import com.frappe.platform.DomainEvent;
import com.frappe.platform.IdGenerator;
import com.frappe.platform.infrastructure.ids.TestIds;
import java.sql.Timestamp;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.time.ZoneOffset;
import java.util.UUID;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.annotation.Import;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.context.DynamicPropertyRegistry;
import org.springframework.test.context.DynamicPropertySource;

/**
 * Stuck publications are detected by the time of their last attempt, not by their publication date. Rows are dated in
 * 2100 so recovery runs of other cached test contexts, which use the real clock, never consider them stuck.
 */
@SpringBootTest
@Import(TestcontainersConfiguration.class)
@ActiveProfiles("local")
class StuckPublicationIntegrationTests {

    @DynamicPropertySource
    static void unreachableNats(DynamicPropertyRegistry registry) {
        registry.add("frappe.nats.url", () -> "nats://localhost:" + TestPorts.closedPort());
    }

    record Probe(UUID eventId, Instant occurredAt, UUID aggregateId, long aggregateVersion, int eventVersion)
            implements DomainEvent {}

    static final Instant NOW = Instant.parse("2100-01-01T12:00:00Z");

    static final Duration STUCK_AFTER = Duration.ofMinutes(5);

    final IdGenerator ids = TestIds.withClock(Clock.fixed(NOW, ZoneOffset.UTC));

    @Autowired
    JdbcClient jdbc;

    @Autowired
    OutboxRecoveryRepository repository;

    @Test
    void inFlightResubmissionOfAnOldEventIsNotReleased() {
        // Given
        var id = insert("RESUBMITTED", NOW.minus(Duration.ofDays(1)), NOW.minusSeconds(10));

        // When
        repository.releaseStuckPublications(NOW.minus(STUCK_AFTER));

        // Then
        assertThat(status(id)).isEqualTo("RESUBMITTED");
    }

    @Test
    void resubmissionWithoutOutcomeAfterTheThresholdIsReleasedAsFailed() {
        // Given
        var id = insert("RESUBMITTED", NOW.minus(Duration.ofDays(1)), NOW.minus(Duration.ofMinutes(6)));

        // When
        repository.releaseStuckPublications(NOW.minus(STUCK_AFTER));

        // Then
        assertThat(status(id)).isEqualTo("FAILED");
    }

    @Test
    void firstAttemptWithoutOutcomeIsJudgedByItsPublicationDate() {
        // Given
        var stuck = insert("PUBLISHED", NOW.minus(Duration.ofMinutes(6)), null);
        var running = insert("PROCESSING", NOW.minusSeconds(10), null);

        // When
        repository.releaseStuckPublications(NOW.minus(STUCK_AFTER));

        // Then
        assertThat(status(stuck)).isEqualTo("FAILED");
        assertThat(status(running)).isEqualTo("PROCESSING");
    }

    private UUID insert(String status, Instant publishedAt, Instant lastResubmittedAt) {
        var id = ids.newId();
        var event = new Probe(ids.newId(), publishedAt, ids.newId(), 1, 1);
        jdbc.sql("""
                        insert into platform.event_publication (id, listener_id, event_type, serialized_event,
                            publication_date, status, completion_attempts, last_resubmission_date)
                        values (?, 'test.listener', ?, ?, ?, ?, 1, ?)
                        """)
                .params(
                        id,
                        Probe.class.getName(),
                        "{\"eventId\":\"" + event.eventId() + "\"}",
                        Timestamp.from(publishedAt),
                        status,
                        lastResubmittedAt == null ? null : Timestamp.from(lastResubmittedAt))
                .update();
        return id;
    }

    private String status(UUID id) {
        return jdbc.sql("select status from platform.event_publication where id = ?")
                .param(id)
                .query(String.class)
                .single();
    }
}
