package com.frappe.platform.infrastructure.nats;

import static org.assertj.core.api.Assertions.assertThat;
import static org.awaitility.Awaitility.await;

import com.frappe.TestNatsConfiguration;
import com.frappe.TestcontainersConfiguration;
import com.frappe.platform.DomainEvent;
import com.frappe.platform.Uuid7;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.time.ZoneOffset;
import java.util.UUID;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.ApplicationEventPublisher;
import org.springframework.context.annotation.Import;
import org.springframework.modulith.events.CompletedEventPublications;
import org.springframework.modulith.events.EventPublication;
import org.springframework.modulith.events.Externalized;
import org.springframework.modulith.events.IncompleteEventPublications;
import org.springframework.modulith.events.core.EventPublicationRegistry;
import org.springframework.transaction.support.TransactionTemplate;
import org.testcontainers.DockerClientFactory;
import org.testcontainers.containers.GenericContainer;

// The JPA publication registry has no migration yet (FAPI-6); let Hibernate create it for this test only.
@SpringBootTest(properties = {"spring.jpa.hibernate.ddl-auto=update", "frappe.nats.publish-timeout=1s"})
@Import({TestcontainersConfiguration.class, TestNatsConfiguration.class})
class NatsUnavailableTests {

    @Externalized
    record GuestLeft(UUID eventId, Instant occurredAt, UUID aggregateId, long aggregateVersion, int eventVersion)
            implements DomainEvent {}

    final Clock clock = Clock.fixed(Instant.parse("2026-09-18T12:00:00Z"), ZoneOffset.UTC);

    @Autowired
    GenericContainer<?> natsContainer;

    @Autowired
    ApplicationEventPublisher events;

    @Autowired
    TransactionTemplate transactions;

    @Autowired
    EventPublicationRegistry registry;

    @Autowired
    IncompleteEventPublications incomplete;

    @Autowired
    CompletedEventPublications completed;

    @Test
    void failsWithinTheTimeoutAndKeepsThePublicationIncompleteForRetry() {
        var event = new GuestLeft(Uuid7.next(clock), clock.instant(), Uuid7.next(clock), 1, 1);
        var docker = DockerClientFactory.instance().client();
        var container = natsContainer.getContainerId();

        docker.pauseContainerCmd(container).exec();
        try {
            transactions.executeWithoutResult(status -> events.publishEvent(event));

            await().atMost(Duration.ofSeconds(4)).until(() -> isFailed(event));
            assertThat(isCompleted(event)).isFalse();
        } finally {
            docker.unpauseContainerCmd(container).exec();
        }

        incomplete.resubmitIncompletePublications(publication -> matches(publication, event));
        await().until(() -> isCompleted(event));
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
}
