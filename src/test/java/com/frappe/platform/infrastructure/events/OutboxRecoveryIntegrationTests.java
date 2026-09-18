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
import java.util.UUID;
import java.util.stream.LongStream;
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
 * connected, so only the scheduled resubmission and the staleness monitor can bring the publication home.
 */
@SpringBootTest(
        properties = {
            "frappe.nats.publish-timeout=1s",
            "frappe.outbox.recovery.interval=500ms",
            "spring.modulith.events.staleness.published=2s",
            "spring.modulith.events.staleness.processing=2s",
            "spring.modulith.events.staleness.resubmitted=2s",
            "spring.modulith.events.staleness.check-interval=500ms"
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

    @Test
    void failedPublicationIsResubmittedOnceNatsRecoversAndArchived() throws Exception {
        // Given
        var event = new CourseFired(ids.newId(), clock.instant(), ids.newId(), 1, 1);
        withNatsPaused(() -> {
            transactions.executeWithoutResult(status -> publisher.publish(event));
            await().atMost(Duration.ofSeconds(5)).until(() -> "FAILED".equals(outboxStatus(event)));
        });

        // When / Then
        await().atMost(Duration.ofSeconds(20)).until(() -> archived(event));
        assertThat(outboxStatus(event)).isNull();
        assertThat(storedMessagesFor(event)).isOne();
    }

    @Test
    void stalePublicationIsMarkedFailedThenResubmittedAndArchived() throws Exception {
        // Given
        var event = new DessertServed(ids.newId(), clock.instant(), ids.newId(), 1, 1);
        withNatsPaused(() -> {
            transactions.executeWithoutResult(status -> publisher.publish(event));
            await().atMost(Duration.ofSeconds(5)).until(() -> "FAILED".equals(outboxStatus(event)));
            // Simulates an instance that stored the publication and died before its listener ran.
            jdbc.update(
                    "update platform.event_publication set status = 'PUBLISHED' where serialized_event like ?",
                    pattern(event));
        });

        // When / Then
        await().atMost(Duration.ofSeconds(20)).until(() -> archived(event));
        assertThat(storedMessagesFor(event)).isOne();
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
