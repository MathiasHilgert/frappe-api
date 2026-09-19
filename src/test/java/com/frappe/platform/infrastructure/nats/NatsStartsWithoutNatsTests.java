package com.frappe.platform.infrastructure.nats;

import static org.assertj.core.api.Assertions.assertThat;
import static org.awaitility.Awaitility.await;

import com.frappe.TestNatsConfiguration;
import com.frappe.TestcontainersConfiguration;
import com.frappe.platform.DomainEvent;
import com.frappe.platform.IdGenerator;
import com.frappe.platform.infrastructure.ids.TestIds;
import com.github.dockerjava.api.model.ExposedPort;
import com.github.dockerjava.api.model.PortBinding;
import com.github.dockerjava.api.model.Ports;
import io.nats.client.Nats;
import java.io.IOException;
import java.net.ServerSocket;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.time.ZoneOffset;
import java.util.UUID;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.ApplicationEventPublisher;
import org.springframework.context.annotation.Import;
import org.springframework.modulith.events.CompletedEventPublications;
import org.springframework.modulith.events.EventPublication;
import org.springframework.modulith.events.Externalized;
import org.springframework.modulith.events.core.EventPublicationRegistry;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.context.DynamicPropertyRegistry;
import org.springframework.test.context.DynamicPropertySource;
import org.springframework.transaction.support.TransactionTemplate;
import org.testcontainers.containers.GenericContainer;

@SpringBootTest(
        properties = {
            "frappe.nats.connection-timeout=500ms",
            "frappe.nats.reconnect-wait=200ms",
            "frappe.nats.publish-timeout=1s"
        })
@Import(TestcontainersConfiguration.class)
@ActiveProfiles("local")
class NatsStartsWithoutNatsTests {

    static final int PORT = freePort();
    static final GenericContainer<?> NATS = TestNatsConfiguration.natsJetStream()
            .withCreateContainerCmdModifier(cmd -> cmd.getHostConfig()
                    .withPortBindings(new PortBinding(
                            Ports.Binding.bindPort(PORT), new ExposedPort(TestNatsConfiguration.NATS_PORT))));

    @DynamicPropertySource
    static void nats(DynamicPropertyRegistry registry) {
        registry.add("frappe.nats.url", () -> "nats://localhost:" + PORT);
    }

    @Externalized
    record OrderPlaced(UUID eventId, Instant occurredAt, UUID aggregateId, long aggregateVersion, int eventVersion)
            implements DomainEvent {}

    final Clock clock = Clock.fixed(Instant.parse("2026-09-18T12:00:00Z"), ZoneOffset.UTC);

    final IdGenerator ids = TestIds.withClock(clock);

    @Autowired
    ApplicationEventPublisher events;

    @Autowired
    TransactionTemplate transactions;

    @Autowired
    EventPublicationRegistry registry;

    @Autowired
    CompletedEventPublications completed;

    @AfterAll
    static void stopNats() {
        NATS.stop();
    }

    @Test
    void startsWithoutNatsAndPublishesPendingEventsOnceItIsUp() throws Exception {
        // Given
        var event = new OrderPlaced(ids.newId(), clock.instant(), ids.newId(), 1, 1);

        transactions.executeWithoutResult(status -> events.publishEvent(event));
        await().atMost(Duration.ofSeconds(5)).until(() -> isFailed(event));

        // When
        NATS.start();

        // Then
        await().atMost(Duration.ofSeconds(20)).until(() -> isCompleted(event));
        try (var connection = Nats.connect("nats://localhost:" + PORT)) {
            var message = connection
                    .jetStreamManagement()
                    .getLastMessage(NatsStreamProvisioner.STREAM, "frappe.platform.order-placed.v1");
            assertThat(message.getHeaders().getFirst("Nats-Msg-Id"))
                    .isEqualTo(event.eventId().toString());
        }
    }

    private boolean isFailed(DomainEvent event) {
        return registry.findIncompletePublications().stream()
                .anyMatch(it -> matches(it, event) && it.getStatus() == EventPublication.Status.FAILED);
    }

    private boolean isCompleted(DomainEvent event) {
        return completed.findAll().stream().anyMatch(it -> matches(it, event));
    }

    private static boolean matches(EventPublication publication, DomainEvent event) {
        return publication.getEvent() instanceof DomainEvent e && e.eventId().equals(event.eventId());
    }

    private static int freePort() {
        try (var socket = new ServerSocket(0)) {
            return socket.getLocalPort();
        } catch (IOException e) {
            throw new IllegalStateException(e);
        }
    }
}
