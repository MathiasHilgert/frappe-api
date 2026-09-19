package com.frappe.platform.infrastructure.mail;

import static org.assertj.core.api.Assertions.assertThat;
import static org.awaitility.Awaitility.await;

import ch.qos.logback.classic.Level;
import ch.qos.logback.classic.Logger;
import ch.qos.logback.classic.spi.ILoggingEvent;
import ch.qos.logback.core.read.ListAppender;
import com.frappe.TestNatsConfiguration;
import com.frappe.TestcontainersConfiguration;
import com.frappe.platform.DomainEvent;
import com.frappe.platform.DomainEventPublisher;
import com.frappe.platform.IdGenerator;
import com.frappe.platform.infrastructure.ids.TestIds;
import com.frappe.platform.mail.MailDeliveryException;
import com.frappe.platform.mail.MailMessage;
import com.frappe.platform.mail.Mailer;
import com.resend.core.exception.ResendException;
import com.resend.core.net.RequestOptions;
import com.resend.services.emails.model.CreateEmailOptions;
import com.resend.services.emails.model.CreateEmailResponse;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.time.ZoneOffset;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.UUID;
import java.util.concurrent.CopyOnWriteArrayList;
import java.util.concurrent.atomic.AtomicInteger;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.slf4j.LoggerFactory;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.test.context.TestConfiguration;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Import;
import org.springframework.context.annotation.Primary;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.modulith.events.ApplicationModuleListener;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.context.DynamicPropertyRegistrar;
import org.springframework.transaction.support.TransactionTemplate;
import org.testcontainers.postgresql.PostgreSQLContainer;

/**
 * Mail is sent by an event listener after the commit, so a provider failure leaves the publication incomplete and the
 * outbox recovery sends it later. Resend is stubbed at the SDK call (its base URL is fixed); nothing reaches the real
 * API. Runs on its own database: recovery jobs of other cached contexts share the reused one and would dead-letter a
 * failed publication of a listener they do not have.
 */
@SpringBootTest(
        properties = {
            "frappe.mail.provider=resend",
            "RESEND_API_KEY=re_not_a_real_key",
            "frappe.outbox.recovery.interval=500ms",
            "frappe.outbox.recovery.stuck-after=2s"
        })
@ActiveProfiles("local")
@Import({TestNatsConfiguration.class, MailOutboxIntegrationTests.Wiring.class})
class MailOutboxIntegrationTests {

    /** A member joined; the example event a welcome mail follows. */
    record MemberJoined(
            UUID eventId, Instant occurredAt, UUID aggregateId, long aggregateVersion, int eventVersion, String email)
            implements DomainEvent {}

    /**
     * The example listener: sends after the commit, with the event id as idempotency key, so a retry after a timeout
     * that Resend did process is not delivered twice.
     */
    static class WelcomeMails {

        private final Mailer mailer;

        WelcomeMails(Mailer mailer) {
            this.mailer = mailer;
        }

        @ApplicationModuleListener
        void on(MemberJoined event) {
            mailer.send(MailMessage.of(
                            "mailprobe/welcome",
                            event.email(),
                            Locale.of("es"),
                            Locale.of("en"),
                            Map.of("name", "Ana", "code", "123456"))
                    .withIdempotencyKey("welcome/" + event.eventId()));
        }
    }

    /** Resend answering an error status until it recovers ({@code 0}). */
    static class FlakyResend implements ResendEmails {

        final AtomicInteger failingStatus = new AtomicInteger();

        final AtomicInteger failedAttempts = new AtomicInteger();

        final List<Delivered> delivered = new CopyOnWriteArrayList<>();

        @Override
        public CreateEmailResponse send(CreateEmailOptions options, RequestOptions request) throws ResendException {
            var status = failingStatus.get();
            if (status != 0) {
                failedAttempts.incrementAndGet();
                throw new ResendException(
                        status, "{\"statusCode\":%d,\"name\":\"error\",\"message\":\"Failed\"}".formatted(status));
            }
            delivered.add(new Delivered(options.getTo(), options.getSubject(), request.getIdempotencyKey()));
            return new CreateEmailResponse("re_" + delivered.size());
        }
    }

    record Delivered(List<String> to, String subject, String idempotencyKey) {}

    @TestConfiguration(proxyBeanMethods = false)
    static class Wiring {

        @Bean
        PostgreSQLContainer postgresContainer() {
            return TestcontainersConfiguration.postgres("frappe_mail_outbox");
        }

        @Bean
        DynamicPropertyRegistrar postgresProperties(PostgreSQLContainer postgres) {
            return TestcontainersConfiguration.connectionProperties(postgres);
        }

        @Bean
        @Primary
        FlakyResend flakyResend() {
            return new FlakyResend();
        }

        @Bean
        WelcomeMails welcomeMails(Mailer mailer) {
            return new WelcomeMails(mailer);
        }
    }

    final Clock clock = Clock.fixed(Instant.parse("2026-09-19T12:00:00Z"), ZoneOffset.UTC);

    final IdGenerator ids = TestIds.withClock(clock);

    @Autowired
    DomainEventPublisher publisher;

    @Autowired
    TransactionTemplate transactions;

    @Autowired
    JdbcTemplate jdbc;

    @Autowired
    FlakyResend resend;

    final ListAppender<ILoggingEvent> logs = new ListAppender<>();

    final Logger root = (Logger) LoggerFactory.getLogger(org.slf4j.Logger.ROOT_LOGGER_NAME);

    @BeforeEach
    void resetResendAndCaptureLogs() {
        resend.failingStatus.set(0);
        resend.failedAttempts.set(0);
        resend.delivered.clear();
        logs.start();
        root.addAppender(logs);
    }

    @AfterEach
    void releaseLogs() {
        root.detachAppender(logs);
    }

    @Test
    void aProviderFailureLeavesThePublicationIncompleteAndRecoverySendsItLater() {
        // Given Resend is down
        resend.failingStatus.set(503);
        var recipient = "ana-" + UUID.randomUUID() + "@example.com";
        var event = new MemberJoined(ids.newId(), clock.instant(), ids.newId(), 1, 1, recipient);

        // When
        transactions.executeWithoutResult(status -> publisher.publish(event));

        // Then the listener failed and the publication stays incomplete
        await().atMost(Duration.ofSeconds(10)).until(() -> resend.failedAttempts.get() >= 1 && incomplete(event));
        assertThat(resend.delivered).isEmpty();

        // When Resend recovers
        resend.failingStatus.set(0);

        // Then a recovery pass sends it, once, with the idempotency key
        await().atMost(Duration.ofSeconds(20)).until(() -> completed(event));
        assertThat(resend.delivered)
                .singleElement()
                .isEqualTo(new Delivered(List.of(recipient), "Tu código de Frappé", "welcome/" + event.eventId()));
        // Logged once per failed attempt, at the listener boundary only (log or rethrow, never both)
        assertThat(errorsAbout(MailDeliveryException.class)).isEqualTo(resend.failedAttempts.get());
    }

    @Test
    void aPermanentRejectionCompletesThePublicationWithoutRetries() {
        // Given Resend rejects the request (422: invalid recipient)
        resend.failingStatus.set(422);
        var event = new MemberJoined(ids.newId(), clock.instant(), ids.newId(), 1, 1, "ana@example.com");

        // When
        transactions.executeWithoutResult(status -> publisher.publish(event));

        // Then the listener completes: one attempt, one ERROR, nothing left for the recovery job
        await().atMost(Duration.ofSeconds(10)).until(() -> completed(event));
        await().during(Duration.ofSeconds(2))
                .atMost(Duration.ofSeconds(4))
                .until(() -> resend.failedAttempts.get() == 1);
        assertThat(errorsAbout(MailRejectedException.class)).isOne();
        assertThat(errorsAbout(MailDeliveryException.class)).isZero();
    }

    // By exception type: cached contexts of other test classes keep logging in the background (NATS reconnects).
    private long errorsAbout(Class<? extends Throwable> type) {
        return logs.list.stream()
                .filter(log -> log.getLevel() == Level.ERROR)
                .filter(log -> log.getThrowableProxy() != null
                        && log.getThrowableProxy().getClassName().equals(type.getName()))
                .count();
    }

    private boolean incomplete(DomainEvent event) {
        var status = jdbc.queryForList(
                "select status from platform.event_publication where serialized_event like ?",
                String.class,
                "%" + event.eventId() + "%");
        return status.size() == 1 && List.of("FAILED", "RESUBMITTED").contains(status.getFirst());
    }

    private boolean completed(DomainEvent event) {
        var archived = jdbc.queryForObject(
                "select count(*) from platform.event_publication_archive where serialized_event like ?",
                Integer.class,
                "%" + event.eventId() + "%");
        return archived != null && archived == 1;
    }
}
