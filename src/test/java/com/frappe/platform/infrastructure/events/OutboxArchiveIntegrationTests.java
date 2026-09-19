package com.frappe.platform.infrastructure.events;

import static org.assertj.core.api.Assertions.assertThat;
import static org.awaitility.Awaitility.await;

import com.frappe.TestNatsConfiguration;
import com.frappe.TestcontainersConfiguration;
import com.frappe.platform.DomainEvent;
import com.frappe.platform.DomainEventPublisher;
import com.frappe.platform.IdGenerator;
import com.frappe.platform.infrastructure.ids.TestIds;
import java.time.Clock;
import java.time.Instant;
import java.time.ZoneOffset;
import java.util.UUID;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.annotation.Import;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.modulith.events.Externalized;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.transaction.support.TransactionTemplate;

/** Completion mode ARCHIVE: once JetStream acks, the publication leaves the outbox for the archive. */
@SpringBootTest
@Import({TestcontainersConfiguration.class, TestNatsConfiguration.class})
@ActiveProfiles("local")
class OutboxArchiveIntegrationTests {

    @Externalized
    record BillSplit(UUID eventId, Instant occurredAt, UUID aggregateId, long aggregateVersion, int eventVersion)
            implements DomainEvent {}

    final Clock clock = Clock.fixed(Instant.parse("2026-09-18T12:00:00Z"), ZoneOffset.UTC);

    final IdGenerator ids = TestIds.withClock(clock);

    @Autowired
    DomainEventPublisher publisher;

    @Autowired
    TransactionTemplate transactions;

    @Autowired
    JdbcTemplate jdbc;

    @Test
    void acknowledgedPublicationMovesToTheArchive() {
        // Given
        var event = new BillSplit(ids.newId(), clock.instant(), ids.newId(), 1, 1);

        // When
        transactions.executeWithoutResult(status -> publisher.publish(event));

        // Then
        await().until(() -> rows("platform.event_publication_archive", event) == 1);
        assertThat(rows("platform.event_publication", event)).isZero();
        assertThat(jdbc.queryForObject(
                        "select status from platform.event_publication_archive where serialized_event like ?",
                        String.class,
                        pattern(event)))
                .isEqualTo("COMPLETED");
    }

    private int rows(String table, DomainEvent event) {
        var rows = jdbc.queryForObject(
                "select count(*) from " + table + " where serialized_event like ?", Integer.class, pattern(event));
        return rows == null ? 0 : rows;
    }

    private static String pattern(DomainEvent event) {
        return "%" + event.eventId() + "%";
    }
}
