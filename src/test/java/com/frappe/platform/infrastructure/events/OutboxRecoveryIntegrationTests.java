package com.frappe.platform.infrastructure.events;

import static org.assertj.core.api.Assertions.assertThat;
import static org.awaitility.Awaitility.await;

import com.frappe.TestNatsConfiguration;
import com.frappe.TestcontainersConfiguration;
import com.frappe.platform.DomainEvent;
import com.frappe.platform.DomainEventPublisher;
import com.frappe.platform.IdGenerator;
import com.frappe.platform.infrastructure.ids.TestIds;
import io.nats.client.JetStreamApiException;
import io.nats.client.JetStreamManagement;
import io.nats.client.Nats;
import io.nats.client.api.MessageInfo;
import java.io.IOException;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.time.ZoneOffset;
import java.util.ArrayList;
import java.util.List;
import java.util.UUID;
import java.util.concurrent.atomic.AtomicReference;
import java.util.stream.LongStream;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.annotation.Import;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.modulith.events.Externalized;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.transaction.support.TransactionTemplate;
import org.testcontainers.DockerClientFactory;
import org.testcontainers.containers.GenericContainer;

/**
 * Recovery without a NATS reconnect: pausing the container fails the publish (timeout) while the client stays
 * connected, so only the scheduled recovery (stuck detection and resubmission) can bring the publication home. The
 * scheduler polls every 100ms here, so the 500ms recovery interval is kept (production polls every 10s).
 */
@SpringBootTest(
        properties = {
            "frappe.nats.publish-timeout=1s",
            "frappe.outbox.recovery.interval=500ms",
            "frappe.outbox.recovery.stuck-after=2s",
            "db-scheduler.polling-interval=100ms"
        })
@Import({TestcontainersConfiguration.class, TestNatsConfiguration.class})
@ActiveProfiles("local")
class OutboxRecoveryIntegrationTests {

    @Externalized
    record CourseFired(UUID eventId, Instant occurredAt, UUID aggregateId, long aggregateVersion, int eventVersion)
            implements DomainEvent {}

    @Externalized
    record DessertServed(UUID eventId, Instant occurredAt, UUID aggregateId, long aggregateVersion, int eventVersion)
            implements DomainEvent {}

    final Clock clock = Clock.fixed(Instant.parse("2026-09-18T12:00:00Z"), ZoneOffset.UTC);

    final IdGenerator ids = TestIds.withClock(clock);

    static final String STREAM = "FRAPPE";

    @Autowired
    GenericContainer<?> natsContainer;

    @Autowired
    DomainEventPublisher publisher;

    @Autowired
    TransactionTemplate transactions;

    @Autowired
    JdbcTemplate jdbc;

    final List<UUID> inserted = new ArrayList<>();

    @Test
    void failedPublicationIsResubmittedOnceNatsRecoversAndArchived() throws Exception {
        // Given
        var event = new CourseFired(ids.newId(), clock.instant(), ids.newId(), 1, 1);
        withNatsPaused(() -> {
            transactions.executeWithoutResult(status -> publisher.publish(event));
            await().atMost(Duration.ofSeconds(5)).until(() -> retriedAtLeastOnce(event));
        });

        // When / Then
        await().atMost(Duration.ofSeconds(20)).until(() -> archived(event));
        assertThat(outboxStatus(event)).isNull();
        assertThat(archivedAttempts(event)).isGreaterThanOrEqualTo(2);
        assertThat(storedMessagesFor(event)).isOne();
    }

    @Test
    void stalePublicationIsMarkedFailedThenResubmittedAndArchived() throws Exception {
        // Given
        var event = new DessertServed(ids.newId(), clock.instant(), ids.newId(), 1, 1);
        withNatsPaused(() -> {
            transactions.executeWithoutResult(status -> publisher.publish(event));
            await().atMost(Duration.ofSeconds(5)).until(() -> retriedAtLeastOnce(event));
            // Simulates an instance that stored the publication and died before its listener ran.
            jdbc.update(
                    "update platform.event_publication set status = 'PUBLISHED' where serialized_event like ?",
                    pattern(event));
        });

        // When / Then
        await().atMost(Duration.ofSeconds(20)).until(() -> archived(event));
        assertThat(storedMessagesFor(event)).isOne();
    }

    @Test
    void unreadableRowsBecomeDeadLettersWhileAValidFailureIsStillRecovered() {
        // Given
        var event = new CourseFired(ids.newId(), clock.instant(), ids.newId(), 1, 1);
        var removedType = new AtomicReference<UUID>();
        var brokenPayload = new AtomicReference<UUID>();
        withNatsPaused(() -> {
            transactions.executeWithoutResult(status -> publisher.publish(event));
            await().atMost(Duration.ofSeconds(5)).until(() -> retriedAtLeastOnce(event));
            // Copied while NATS is still paused: once it is back, the valid row may be archived at any moment.
            // Older than the valid row, so they come first in every selection.
            removedType.set(insertFailedCopyOf(event, "com.frappe.removed.TableMerged", "{}"));
            // Valid JSON (the generated event_id column added by FAPI-9 needs it to parse) with a type Jackson cannot
            // bind to CourseFired.occurredAt: no database failure, but a DatabindException on deserialize, the same
            // UNREADABLE_PAYLOAD a real corrupted payload would cause.
            brokenPayload.set(insertFailedCopyOf(
                    event,
                    CourseFired.class.getName(),
                    "{\"eventId\":\"" + ids.newId() + "\",\"occurredAt\":\"not-an-instant\"}"));
        });

        // When / Then
        await().atMost(Duration.ofSeconds(20)).until(() -> archived(event));
        await().atMost(Duration.ofSeconds(10))
                .until(() ->
                        deadLetterReason(removedType.get()) != null && deadLetterReason(brokenPayload.get()) != null);
        assertThat(deadLetterReason(removedType.get())).isEqualTo("UNKNOWN_EVENT_TYPE");
        assertThat(deadLetterReason(brokenPayload.get())).isEqualTo("UNREADABLE_PAYLOAD");
    }

    @AfterEach
    void deleteInsertedRows() {
        jdbc.update("delete from platform.event_publication_dead_letter where id = any(?)", (Object)
                inserted.toArray(UUID[]::new));
        jdbc.update("delete from platform.event_publication where id = any(?)", (Object) inserted.toArray(UUID[]::new));
    }

    // A failed row for the same listener as a real publication, as an older deployment would have left it.
    private UUID insertFailedCopyOf(DomainEvent event, String eventType, String payload) {
        var id = ids.newId();
        inserted.add(id);
        jdbc.update("""
                insert into platform.event_publication (id, listener_id, event_type, serialized_event,
                    publication_date, status, completion_attempts)
                select ?, listener_id, ?, ?, publication_date - interval '1 hour', 'FAILED', 0
                  from platform.event_publication
                 where serialized_event like ?
                """, id, eventType, payload, pattern(event));
        return id;
    }

    private String deadLetterReason(UUID id) {
        return jdbc
                .queryForList(
                        "select reason from platform.event_publication_dead_letter where id = ?", String.class, id)
                .stream()
                .findFirst()
                .orElse(null);
    }

    private void withNatsPaused(Runnable action) {
        var docker = DockerClientFactory.instance().client();
        var container = natsContainer.getContainerId();
        docker.pauseContainerCmd(container).exec();
        try {
            action.run();
        } finally {
            docker.unpauseContainerCmd(container).exec();
        }
    }

    // While NATS is paused the 500ms recovery keeps retrying, so the row alternates between FAILED and RESUBMITTED
    // (an async failure stays RESUBMITTED until stuck-after); with two or more attempts the recovery already retried.
    private boolean retriedAtLeastOnce(DomainEvent event) {
        var status = outboxStatus(event);
        return ("FAILED".equals(status) || "RESUBMITTED".equals(status)) && outboxAttempts(event) >= 2;
    }

    private int outboxAttempts(DomainEvent event) {
        return jdbc
                .queryForList(
                        "select completion_attempts from platform.event_publication where serialized_event like ?",
                        Integer.class,
                        pattern(event))
                .stream()
                .findFirst()
                .orElse(0);
    }

    private int archivedAttempts(DomainEvent event) {
        return jdbc.queryForObject(
                "select completion_attempts from platform.event_publication_archive where serialized_event like ?",
                Integer.class,
                pattern(event));
    }

    private String outboxStatus(DomainEvent event) {
        return jdbc
                .queryForList(
                        "select status from platform.event_publication where serialized_event like ?",
                        String.class,
                        pattern(event))
                .stream()
                .findFirst()
                .orElse(null);
    }

    private boolean archived(DomainEvent event) {
        var rows = jdbc.queryForObject(
                "select count(*) from platform.event_publication_archive where serialized_event like ?",
                Integer.class,
                pattern(event));
        return rows != null && rows == 1;
    }

    // Counts by Nats-Msg-Id: the reused test database may hold failed publications of the same event type from
    // earlier runs, which the recovery job also delivers to this context's NATS.
    private long storedMessagesFor(DomainEvent event) throws Exception {
        try (var connection = Nats.connect(TestNatsConfiguration.natsUrl(natsContainer))) {
            var streams = connection.jetStreamManagement();
            var lastSequence = streams.getStreamInfo(STREAM).getStreamState().getLastSequence();
            return LongStream.rangeClosed(1, lastSequence)
                    .mapToObj(sequence -> message(streams, sequence))
                    .filter(message -> event.eventId()
                            .toString()
                            .equals(message.getHeaders().getFirst("Nats-Msg-Id")))
                    .count();
        }
    }

    private static MessageInfo message(JetStreamManagement streams, long sequence) {
        try {
            return streams.getMessage(STREAM, sequence);
        } catch (IOException | JetStreamApiException e) {
            throw new IllegalStateException("Reading stream message " + sequence + " failed", e);
        }
    }

    private static String pattern(DomainEvent event) {
        return "%" + event.eventId() + "%";
    }
}
