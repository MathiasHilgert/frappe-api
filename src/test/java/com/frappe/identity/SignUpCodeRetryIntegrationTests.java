package com.frappe.identity;

import static org.assertj.core.api.Assertions.assertThat;
import static org.awaitility.Awaitility.await;

import com.frappe.TestNatsConfiguration;
import com.frappe.TestValkeyConfiguration;
import com.frappe.TestcontainersConfiguration;
import com.frappe.identity.application.StartSignUp;
import com.frappe.identity.domain.EmailAddress;
import com.frappe.platform.KeyedDigests;
import com.frappe.platform.SecretKey;
import com.frappe.platform.ShortLivedSecretStore;
import com.frappe.platform.mail.MailDeliveryException;
import java.net.InetAddress;
import java.time.Duration;
import java.util.List;
import java.util.Locale;
import java.util.UUID;
import java.util.concurrent.CopyOnWriteArrayList;
import java.util.concurrent.atomic.AtomicInteger;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.test.context.TestConfiguration;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Import;
import org.springframework.context.annotation.Primary;
import org.springframework.data.redis.core.StringRedisTemplate;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.context.DynamicPropertyRegistrar;
import org.testcontainers.postgresql.PostgreSQLContainer;

/**
 * A transient mail failure leaves the code's publication incomplete; the outbox retries it, and the retry mails a fresh
 * code without spending the address's issue cap. Runs on its own database, so recovery jobs of other cached contexts
 * never pick up its failed publications.
 */
@SpringBootTest(properties = {"frappe.outbox.recovery.interval=500ms", "frappe.outbox.recovery.stuck-after=2s"})
@ActiveProfiles("local")
@Import({TestValkeyConfiguration.class, TestNatsConfiguration.class, SignUpCodeRetryIntegrationTests.Wiring.class})
class SignUpCodeRetryIntegrationTests {

    /** A code mailer failing transiently while {@link #failing} is set. */
    static class FlakyCodeMailer implements CodeMailer {

        final AtomicInteger failing = new AtomicInteger();

        final AtomicInteger failedAttempts = new AtomicInteger();

        final List<CodeMail> sent = new CopyOnWriteArrayList<>();

        @Override
        public void send(CodeMail mail) {
            if (failing.get() != 0) {
                failedAttempts.incrementAndGet();
                throw new MailDeliveryException("Mail provider unavailable");
            }
            sent.add(mail);
        }

        @Override
        public void sendAccountExists(AccountExistsMail mail) {
            throw new UnsupportedOperationException();
        }
    }

    @TestConfiguration(proxyBeanMethods = false)
    static class Wiring {

        @Bean
        PostgreSQLContainer postgresContainer() {
            return TestcontainersConfiguration.postgres("frappe_sign_up_retry");
        }

        @Bean
        DynamicPropertyRegistrar postgresProperties(PostgreSQLContainer postgres) {
            return TestcontainersConfiguration.connectionProperties(postgres);
        }

        @Bean
        @Primary
        FlakyCodeMailer flakyCodeMailer() {
            return new FlakyCodeMailer();
        }
    }

    @Autowired
    StartSignUp startSignUp;

    @Autowired
    FlakyCodeMailer mailer;

    @Autowired
    ShortLivedSecretStore secrets;

    @Autowired
    KeyedDigests digests;

    @Autowired
    StringRedisTemplate valkey;

    @Test
    void aRetryAfterATransientFailureMailsAWorkingCodeAndKeepsTheIssueCount() throws Exception {
        // Given the mail provider is down
        mailer.failing.set(1);
        var email = "ana-" + UUID.randomUUID() + "@example.com";
        var subject = digests.subjectOf("identity.email", email);
        var key = SecretKey.of(IdentitySecrets.SIGN_UP, subject);

        // When
        startSignUp.start(new EmailAddress(email), InetAddress.ofLiteral("10.41.0.1"), Locale.of("en"));
        await().atMost(Duration.ofSeconds(10)).until(() -> mailer.failedAttempts.get() >= 1);
        mailer.failing.set(0);

        // Then the outbox retries and the retry's code works
        await().atMost(Duration.ofSeconds(20)).until(() -> sentTo(email).size() == 1);
        assertThat(secrets.consume(key, sentTo(email).getFirst().code())).isTrue();
        assertThat(valkey.opsForZSet().zCard("frappe:secret-issues:identity:sign-up:" + subject))
                .isOne();
    }

    private List<CodeMail> sentTo(String email) {
        return mailer.sent.stream()
                .filter(mail -> mail.recipient().equals(email))
                .toList();
    }
}
