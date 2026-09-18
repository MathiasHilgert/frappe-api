package com.frappe.platform.infrastructure.events;

import static org.assertj.core.api.Assertions.assertThat;

import com.frappe.TestcontainersConfiguration;
import com.frappe.platform.IdGenerator;
import com.frappe.platform.infrastructure.ids.TestIds;
import java.sql.Timestamp;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.time.ZoneOffset;
import java.util.ArrayList;
import java.util.List;
import java.util.UUID;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.annotation.Import;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.context.DynamicPropertyRegistry;
import org.springframework.test.context.DynamicPropertySource;

/**
 * Retry selection and dead letters on the real outbox tables. Rows are dated in 2100 so recovery runs of other cached
 * test contexts (real clock) never select them; assertions read the tables by the test's own ids.
 */
@SpringBootTest
@Import(TestcontainersConfiguration.class)
@ActiveProfiles("local")
class DeadLetterIntegrationTests {

    @DynamicPropertySource
    static void unreachableNats(DynamicPropertyRegistry registry) {
        registry.add("frappe.nats.url", () -> "nats://localhost:" + TestPorts.closedPort());
    }

    static final Instant NOW = Instant.parse("2100-01-01T12:00:00Z");

    static final Duration BASE_BACKOFF = Duration.ofMinutes(1);

    static final Duration MAX_BACKOFF = Duration.ofHours(1);

    final IdGenerator ids = TestIds.withClock(Clock.fixed(NOW, ZoneOffset.UTC));

    @Autowired
    JdbcClient jdbc;

    @Autowired
    OutboxRecoveryRepository repository;

    final List<UUID> inserted = new ArrayList<>();

    // Rows dated in 2100 are never recovered by the real clock, so they would stay and skew later runs.
    @AfterEach
    void deleteInsertedRows() {
        for (var table : List.of("platform.event_publication", "platform.event_publication_dead_letter")) {
            jdbc.sql("delete from " + table + " where id = any(?)")
                    .param(inserted.toArray(UUID[]::new))
                    .update();
        }
    }

    @Test
    void exhaustedPublicationMovesToTheDeadLetterTable() {
        // Given
        var exhausted = insertFailed(NOW.minus(Duration.ofDays(1)), 3, NOW.minus(Duration.ofHours(2)));
        var retryable = insertFailed(NOW.minus(Duration.ofDays(1)), 2, NOW.minus(Duration.ofHours(2)));

        // When
        var moved = repository.deadLetterExhausted(3, NOW);

        // Then
        assertThat(moved)
                .filteredOn(letter -> letter.publicationId().equals(exhausted))
                .singleElement()
                .satisfies(letter -> {
                    assertThat(letter.listenerId()).isEqualTo("test.listener");
                    assertThat(letter.eventType()).isEqualTo("com.frappe.Probe");
                    assertThat(letter.completionAttempts()).isEqualTo(3);
                    assertThat(letter.reason()).isEqualTo(DeadLetterReason.MAX_ATTEMPTS_EXHAUSTED);
                });
        assertThat(inOutbox(exhausted)).isFalse();
        assertThat(deadLetterReason(exhausted)).isEqualTo("MAX_ATTEMPTS_EXHAUSTED");
        assertThat(inOutbox(retryable)).isTrue();
        assertThat(repository.countDeadLetters()).isPositive();
    }

    @Test
    void publicationsInBackoffNeverStarveANewerFailure() {
        // Given
        // Old, often retried rows tried a minute ago are still backing off (8 minutes after 4 attempts).
        for (var i = 0; i < 3; i++) {
            insertFailed(NOW.minus(Duration.ofDays(1)), 4, NOW.minus(Duration.ofMinutes(1)));
        }
        var fresh = insertFailed(NOW.minus(Duration.ofMinutes(2)), 0, null);

        // When
        var selected = repository.findRetryable(NOW, 1, BASE_BACKOFF, MAX_BACKOFF);

        // Then
        assertThat(selected).containsExactly(fresh);
    }

    @Test
    void leastRecentlyAttemptedPublicationIsRetriedFirst() {
        // Given
        var triedLongAgo = insertFailed(NOW.minus(Duration.ofDays(1)), 1, NOW.minus(Duration.ofHours(3)));
        var triedRecently = insertFailed(NOW.minus(Duration.ofDays(2)), 1, NOW.minus(Duration.ofHours(2)));

        // When
        var selected = repository.findRetryable(NOW, 2, BASE_BACKOFF, MAX_BACKOFF);

        // Then
        assertThat(selected).containsSubsequence(triedLongAgo, triedRecently);
    }

    private UUID insertFailed(Instant publishedAt, int attempts, Instant lastAttemptAt) {
        var id = ids.newId();
        inserted.add(id);
        jdbc.sql("""
                        insert into platform.event_publication (id, listener_id, event_type, serialized_event,
                            publication_date, status, completion_attempts, last_resubmission_date)
                        values (?, 'test.listener', 'com.frappe.Probe', '{}', ?, 'FAILED', ?, ?)
                        """)
                .params(
                        id,
                        Timestamp.from(publishedAt),
                        attempts,
                        lastAttemptAt == null ? null : Timestamp.from(lastAttemptAt))
                .update();
        return id;
    }

    private boolean inOutbox(UUID id) {
        return jdbc.sql("select count(*) from platform.event_publication where id = ?")
                        .param(id)
                        .query(Long.class)
                        .single()
                == 1;
    }

    private String deadLetterReason(UUID id) {
        return jdbc.sql("select reason from platform.event_publication_dead_letter where id = ?")
                .param(id)
                .query(String.class)
                .single();
    }
}
