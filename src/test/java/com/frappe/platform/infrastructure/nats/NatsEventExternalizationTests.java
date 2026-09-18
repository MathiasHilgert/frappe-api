package com.frappe.platform.infrastructure.nats;

import static org.assertj.core.api.Assertions.assertThat;
import static org.awaitility.Awaitility.await;

import com.frappe.TestcontainersConfiguration;
import com.frappe.platform.DomainEvent;
import com.frappe.platform.Uuid7;
import io.nats.client.Connection;
import io.nats.client.JetStreamManagement;
import io.nats.client.api.StreamInfoOptions;
import java.nio.charset.StandardCharsets;
import java.time.Clock;
import java.time.Instant;
import java.time.ZoneOffset;
import java.util.UUID;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.ApplicationEventPublisher;
import org.springframework.context.annotation.Import;
import org.springframework.modulith.events.CompletedEventPublications;
import org.springframework.modulith.events.Externalized;
import org.springframework.transaction.support.TransactionTemplate;

// The JPA publication registry has no migration yet (FAPI-6); let Hibernate create it for this test only.
@SpringBootTest(properties = "spring.jpa.hibernate.ddl-auto=update")
@Import(TestcontainersConfiguration.class)
class NatsEventExternalizationTests {

    static final String SUBJECT = "frappe.platform.tab-closed.v3";

    @Externalized
    record TabClosed(
            UUID eventId, Instant occurredAt, UUID aggregateId, long aggregateVersion, int eventVersion, String note)
            implements DomainEvent {}

    final Clock clock = Clock.fixed(Instant.parse("2026-09-18T12:00:00Z"), ZoneOffset.UTC);

    @Autowired
    ApplicationEventPublisher events;

    @Autowired
    TransactionTemplate transactions;

    @Autowired
    CompletedEventPublications completed;

    @Autowired
    Connection connection;

    @Test
    void publishesExactlyOneMessageWithEnvelopeHeadersOnceAcked() throws Exception {
        var event = tabClosed();
        var before = storedOnSubject();

        publish(event);

        await().until(() -> isCompleted(event));
        assertThat(storedOnSubject()).isEqualTo(before + 1);
        var message = jsm().getLastMessage(NatsStreamProvisioner.STREAM, SUBJECT);
        var headers = message.getHeaders();
        assertThat(headers.getFirst("Nats-Msg-Id")).isEqualTo(event.eventId().toString());
        assertThat(headers.getFirst("Frappe-Event-Type")).isEqualTo("platform.tab-closed");
        assertThat(headers.getFirst("Frappe-Event-Version")).isEqualTo("3");
        assertThat(headers.getFirst("Frappe-Aggregate-Id"))
                .isEqualTo(event.aggregateId().toString());
        assertThat(headers.getFirst("Frappe-Aggregate-Version")).isEqualTo("7");
        assertThat(headers.getFirst("Frappe-Occurred-At")).isEqualTo("2026-09-18T12:00:00Z");
        assertThat(new String(message.getData(), StandardCharsets.UTF_8))
                .contains("\"eventId\":\"" + event.eventId() + "\"")
                .contains("\"note\":\"closed by waiter\"");
    }

    @Test
    void storesAnEventPublishedTwiceWithinTheDuplicateWindowOnce() throws Exception {
        var event = tabClosed();
        var before = storedOnSubject();

        publish(event);
        publish(event);

        await().until(() -> completedCount(event) == 2);
        assertThat(storedOnSubject()).isEqualTo(before + 1);
    }

    private TabClosed tabClosed() {
        return new TabClosed(Uuid7.next(clock), clock.instant(), Uuid7.next(clock), 7, 3, "closed by waiter");
    }

    private void publish(DomainEvent event) {
        transactions.executeWithoutResult(status -> events.publishEvent(event));
    }

    private boolean isCompleted(DomainEvent event) {
        return completedCount(event) > 0;
    }

    private long completedCount(DomainEvent event) {
        return completed.findAll().stream()
                .filter(it ->
                        it.getEvent() instanceof DomainEvent e && e.eventId().equals(event.eventId()))
                .count();
    }

    private long storedOnSubject() throws Exception {
        var state = jsm().getStreamInfo(NatsStreamProvisioner.STREAM, StreamInfoOptions.filterSubjects(SUBJECT))
                .getStreamState();
        return state.getSubjectMap().getOrDefault(SUBJECT, 0L);
    }

    private JetStreamManagement jsm() throws Exception {
        return connection.jetStreamManagement();
    }
}
