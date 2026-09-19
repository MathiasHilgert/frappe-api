package com.frappe.identity;

import static com.frappe.platform.infrastructure.metrics.BusinessMetricAssert.assertThatBusinessMetric;
import static com.frappe.platform.infrastructure.metrics.BusinessMetricAssert.assertValidBusinessMetrics;
import static org.assertj.core.api.Assertions.assertThat;
import static org.awaitility.Awaitility.await;

import com.frappe.TestNatsConfiguration;
import com.frappe.TestValkeyConfiguration;
import com.frappe.TestcontainersConfiguration;
import com.frappe.identity.application.IdentityRefusal;
import com.frappe.identity.application.StartSignUp;
import com.frappe.identity.domain.EmailAddress;
import com.frappe.identity.domain.SignUpCapped;
import com.frappe.identity.domain.SignUpStarted;
import com.frappe.platform.KeyedDigests;
import com.frappe.platform.Result;
import com.frappe.platform.SecretKey;
import com.frappe.platform.ShortLivedSecretStore;
import io.micrometer.core.instrument.MeterRegistry;
import java.net.InetAddress;
import java.net.UnknownHostException;
import java.time.Duration;
import java.util.List;
import java.util.Locale;
import java.util.UUID;
import java.util.concurrent.ThreadLocalRandom;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.TestConfiguration;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Import;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.modulith.test.ApplicationModuleTest;
import org.springframework.test.context.ActiveProfiles;

/**
 * Starting a sign-up through identity alone, on real Postgres and Valkey, with the code mails recorded instead of sent:
 * the request records the sign-up and counts the caps; identity's listener stores and mails the code after the commit.
 */
@ApplicationModuleTest
@Import({
    TestcontainersConfiguration.class,
    TestValkeyConfiguration.class,
    TestNatsConfiguration.class,
    SignUpModuleTests.Mails.class
})
@ActiveProfiles("local")
class SignUpModuleTests {

    private static final Duration MAIL_WAIT = Duration.ofSeconds(10);

    @TestConfiguration(proxyBeanMethods = false)
    static class Mails {

        @Bean
        RecordingCodeMailer recordingCodeMailer() {
            return new RecordingCodeMailer();
        }
    }

    private final StartSignUp startSignUp;
    private final RecordingCodeMailer mails;
    private final ShortLivedSecretStore secrets;
    private final KeyedDigests digests;
    private final JdbcTemplate jdbc;
    private final MeterRegistry meters;

    @Autowired
    SignUpModuleTests(
            StartSignUp startSignUp,
            RecordingCodeMailer mails,
            ShortLivedSecretStore secrets,
            KeyedDigests digests,
            JdbcTemplate jdbc,
            MeterRegistry meters) {
        this.startSignUp = startSignUp;
        this.mails = mails;
        this.secrets = secrets;
        this.digests = digests;
        this.jdbc = jdbc;
        this.meters = meters;
    }

    @Test
    void aNewAddressGetsOneSignUpAndOneSixDigitCodeInItsLanguage() {
        // Given
        var email = newEmail();

        // When
        var result = startSignUp.start(new EmailAddress(email), newClientAddress(), Locale.of("es"));

        // Then
        assertThat(result).isEqualTo(Result.success(StartSignUp.Outcome.STARTED));
        assertThat(signUpRows(email)).isOne();
        var mail = awaitCodeMails(email, 1).getFirst();
        assertThat(mail.purpose()).isEqualTo(CodePurpose.SIGN_UP);
        assertThat(mail.locale()).isEqualTo(Locale.of("es"));
        assertThat(mail.code()).matches("\\d{6}");
        assertThat(mail.validFor()).isEqualTo(Duration.ofMinutes(15));
    }

    @Test
    void aSecondStartReplacesTheEarlierCodeAndKeepsOneSignUpInTheLatestLanguage() {
        // Given
        var email = newEmail();
        startSignUp.start(new EmailAddress(email), newClientAddress(), Locale.of("es"));
        var first = awaitCodeMails(email, 1).getFirst().code();

        // When
        startSignUp.start(new EmailAddress(email.toUpperCase(Locale.ROOT)), newClientAddress(), Locale.of("pt"));

        // Then
        var second = awaitCodeMails(email, 2).getLast();
        assertThat(second.locale()).isEqualTo(Locale.of("pt"));
        assertThat(signUpRows(email)).isOne();
        var key = SecretKey.of(IdentitySecrets.SIGN_UP, subjectOf(email));
        assertThat(secrets.consume(key, first)).isFalse();
        assertThat(secrets.consume(key, second.code())).isTrue();
    }

    @Test
    void theSixthStartWithinAnHourIsAcceptedButMailsNothing() {
        // Given five starts, each mailed
        var email = newEmail();
        for (var start = 1; start <= 5; start++) {
            startSignUp.start(new EmailAddress(email), newClientAddress(), Locale.of("en"));
            awaitCodeMails(email, start);
        }
        var startedAt = startedAt(email);

        // When
        var result = startSignUp.start(new EmailAddress(email), newClientAddress(), Locale.of("es"));

        // Then nothing was written and no code is mailed
        assertThat(result).isEqualTo(Result.success(StartSignUp.Outcome.CAPPED));
        assertThat(startedAt(email)).isEqualTo(startedAt);
        await().during(Duration.ofSeconds(1))
                .atMost(Duration.ofSeconds(3))
                .until(() -> codeMailsTo(email).size() == 5);
    }

    @Test
    void theTwentyFirstStartFromOneClientAddressWithinAnHourIsRefused() {
        // Given
        var clientAddress = newClientAddress();
        for (var start = 1; start <= 20; start++) {
            assertThat(startSignUp.start(new EmailAddress(newEmail()), clientAddress, Locale.of("en")))
                    .isEqualTo(Result.success(StartSignUp.Outcome.STARTED));
        }
        var email = newEmail();

        // When
        var result = startSignUp.start(new EmailAddress(email), clientAddress, Locale.of("en"));

        // Then
        assertThat(result).isEqualTo(Result.failure(new IdentityRefusal.TooManyAttempts()));
        assertThat(signUpRows(email)).isZero();
    }

    @Test
    void theStoredEventHoldsTheSignUpIdAndNeitherTheAddressNorTheCode() {
        // Given
        var email = newEmail();

        // When
        startSignUp.start(new EmailAddress(email), newClientAddress(), Locale.of("en"));

        // Then
        var code = awaitCodeMails(email, 1).getFirst().code();
        var signUpId = jdbc.queryForObject(
                "select id from identity.sign_up where email_subject = ?", UUID.class, subjectOf(email));
        var stored = storedEvents(signUpId);
        assertThat(stored)
                .singleElement()
                .satisfies(event -> assertThat(event)
                        .contains(signUpId.toString())
                        .doesNotContainIgnoringCase(email)
                        .doesNotContain(code));
    }

    @Test
    void anAcceptedStartCountsOneStartedSignUp() {
        // Given
        var before = startedSignUps();

        // When
        startSignUp.start(new EmailAddress(newEmail()), newClientAddress(), Locale.of("en"));

        // Then
        assertThatBusinessMetric(meters, "frappe.identity.sign_ups.started").hasCount(before + 1);
    }

    @Test
    void aCappedStartIsCountedWithoutNamingTheAddress() {
        // Given an address at its issue cap
        var email = newEmail();
        for (var start = 1; start <= 5; start++) {
            startSignUp.start(new EmailAddress(email), newClientAddress(), Locale.of("en"));
        }
        var before = counted("frappe.identity.sign_ups.capped");

        // When
        startSignUp.start(new EmailAddress(email), newClientAddress(), Locale.of("en"));

        // Then
        assertThatBusinessMetric(meters, "frappe.identity.sign_ups.capped").hasCount(before + 1);
        assertThat(meters.find("frappe.identity.sign_ups.capped")
                        .counter()
                        .getId()
                        .getTags())
                .isEmpty();
    }

    @Test
    void theSignUpMetricsAreValid() {
        assertValidBusinessMetrics(SignUpStarted.class, SignUpCapped.class);
    }

    private long counted(String name) {
        var counter = meters.find(name).counter();
        return counter == null ? 0 : (long) counter.count();
    }

    private long startedSignUps() {
        var counter = meters.find("frappe.identity.sign_ups.started").counter();
        return counter == null ? 0 : (long) counter.count();
    }

    private List<CodeMail> awaitCodeMails(String email, int count) {
        await().atMost(MAIL_WAIT).until(() -> codeMailsTo(email).size() >= count);
        return codeMailsTo(email);
    }

    private List<CodeMail> codeMailsTo(String email) {
        return mails.codeMails().stream()
                .filter(mail -> mail.recipient().equalsIgnoreCase(email))
                .toList();
    }

    private UUID subjectOf(String email) {
        return digests.subjectOf("identity.email", email.toLowerCase(Locale.ROOT));
    }

    private int signUpRows(String email) {
        var rows = jdbc.queryForObject(
                "select count(*) from identity.sign_up where email_subject = ?", Integer.class, subjectOf(email));
        return rows == null ? 0 : rows;
    }

    private Object startedAt(String email) {
        return jdbc.queryForObject(
                "select started_at from identity.sign_up where email_subject = ?", Object.class, subjectOf(email));
    }

    // The outbox and its archive: the publication may already be completed and archived.
    private List<String> storedEvents(UUID signUpId) {
        var pattern = "%" + signUpId + "%";
        return jdbc.queryForList(
                "select serialized_event from platform.event_publication where serialized_event like ?"
                        + " union all select serialized_event from platform.event_publication_archive"
                        + " where serialized_event like ?",
                String.class,
                pattern,
                pattern);
    }

    private static String newEmail() {
        return "ana-" + UUID.randomUUID() + "@Example.com";
    }

    private static InetAddress newClientAddress() {
        var random = ThreadLocalRandom.current();
        try {
            return InetAddress.getByAddress(new byte[] {
                10, (byte) random.nextInt(256), (byte) random.nextInt(256), (byte) random.nextInt(1, 255)
            });
        } catch (UnknownHostException impossible) {
            throw new IllegalStateException(impossible);
        }
    }
}
