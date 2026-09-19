package com.frappe.identity;

import static com.frappe.platform.infrastructure.metrics.BusinessMetricAssert.assertThatBusinessMetric;
import static com.frappe.platform.infrastructure.metrics.BusinessMetricAssert.assertValidBusinessMetrics;
import static org.assertj.core.api.Assertions.assertThat;
import static org.awaitility.Awaitility.await;

import com.frappe.TestNatsConfiguration;
import com.frappe.TestValkeyConfiguration;
import com.frappe.TestcontainersConfiguration;
import com.frappe.identity.application.CompleteSignUp;
import com.frappe.identity.application.IdentityRefusal;
import com.frappe.identity.application.StartSignUp;
import com.frappe.identity.domain.BreachStatus;
import com.frappe.identity.domain.BreachedPasswords;
import com.frappe.identity.domain.EmailAddress;
import com.frappe.identity.domain.PasswordRejected;
import com.frappe.identity.domain.RecoveryCodesIssued;
import com.frappe.platform.KeyedDigests;
import com.frappe.platform.Result;
import com.frappe.platform.SecretKey;
import com.frappe.platform.ShortLivedSecretStore;
import io.micrometer.core.instrument.MeterRegistry;
import java.net.InetAddress;
import java.net.UnknownHostException;
import java.time.Duration;
import java.util.Arrays;
import java.util.List;
import java.util.Locale;
import java.util.UUID;
import java.util.concurrent.ThreadLocalRandom;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.TestConfiguration;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Import;
import org.springframework.context.annotation.Primary;
import org.springframework.data.redis.core.StringRedisTemplate;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.modulith.test.ApplicationModuleTest;
import org.springframework.modulith.test.AssertablePublishedEvents;
import org.springframework.test.context.ActiveProfiles;

/**
 * Completing a sign-up through identity alone, on real Postgres and Valkey, with a breach corpus that knows one
 * password and code mails recorded instead of sent.
 */
@ApplicationModuleTest
@Import({
    TestcontainersConfiguration.class,
    TestValkeyConfiguration.class,
    TestNatsConfiguration.class,
    CompleteSignUpModuleTests.Doubles.class
})
@ActiveProfiles("local")
class CompleteSignUpModuleTests {

    static final String PASSWORD = "a long enough passphrase 42";

    static final String BREACHED_PASSWORD = "password1234567890";

    private static final String CODE = "123456";

    private static final Duration MAIL_WAIT = Duration.ofSeconds(10);

    @TestConfiguration(proxyBeanMethods = false)
    static class Doubles {

        @Bean
        RecordingCodeMailer recordingCodeMailer() {
            return new RecordingCodeMailer();
        }

        @Bean
        @Primary
        BreachedPasswords oneBreachedPassword() {
            return password ->
                    password.value().equals(BREACHED_PASSWORD) ? BreachStatus.BREACHED : BreachStatus.NOT_FOUND;
        }
    }

    private final CompleteSignUp completeSignUp;
    private final StartSignUp startSignUp;
    private final RecordingCodeMailer mails;
    private final ShortLivedSecretStore secrets;
    private final KeyedDigests digests;
    private final JdbcTemplate jdbc;
    private final StringRedisTemplate valkey;
    private final MeterRegistry meters;

    @Autowired
    CompleteSignUpModuleTests(
            CompleteSignUp completeSignUp,
            StartSignUp startSignUp,
            RecordingCodeMailer mails,
            ShortLivedSecretStore secrets,
            KeyedDigests digests,
            JdbcTemplate jdbc,
            StringRedisTemplate valkey,
            MeterRegistry meters) {
        this.completeSignUp = completeSignUp;
        this.startSignUp = startSignUp;
        this.mails = mails;
        this.secrets = secrets;
        this.digests = digests;
        this.jdbc = jdbc;
        this.valkey = valkey;
        this.meters = meters;
    }

    @Test
    void theRightCodeRegistersAnActivePersonWithEightRecoveryCodesStoredAsDigestsOnly() {
        // Given a code mailed by a start
        var email = newEmail();
        startSignUp.start(new EmailAddress(email), newClientAddress(), Locale.of("es"));
        await().atMost(MAIL_WAIT).until(() -> codeMailsTo(email).size() == 1);
        var code = codeMailsTo(email).getFirst().code();

        // When
        var result = complete(request(email, code), Locale.of("es"));

        // Then
        var registration = result.orElseThrow(refusal -> new AssertionError(refusal.toString()));
        assertThat(registration.recoveryCodes())
                .hasSize(8)
                .doesNotHaveDuplicates()
                .allMatch(recoveryCode -> recoveryCode.matches("[0-9A-HJKMNP-TV-Z]{10}"));
        var person = jdbc.queryForMap("select * from identity.person where id = ?", registration.personId());
        assertThat(person)
                .containsEntry("email", email)
                .containsEntry("status", "ACTIVE")
                .containsEntry("credential_epoch", 1L)
                .containsEntry("given_name", "Ana")
                .containsEntry("family_name", "Pérez")
                .containsEntry("preferred_locale", "es")
                .containsEntry("terms_version", "2026-09-19")
                .containsEntry("privacy_version", "2026-09-19")
                .containsEntry("version", 0L);
        assertThat((String) person.get("password_hash")).startsWith("$argon2id$").doesNotContain(PASSWORD);
        assertThat(person.get("legal_accepted_at")).isNotNull().isEqualTo(person.get("registered_at"));
        var storedDigests = jdbc.queryForList(
                "select digest from identity.recovery_code where person_id = ? and used_at is null",
                String.class,
                registration.personId());
        assertThat(storedDigests)
                .containsExactlyInAnyOrderElementsOf(registration.recoveryCodes().stream()
                        .map(recoveryCode -> digests.digestOf(
                                "identity.recovery-code", registration.personId() + ":" + recoveryCode))
                        .toList())
                .doesNotContainAnyElementsOf(registration.recoveryCodes());
    }

    @Test
    void personRegisteredCarriesThePersonIdOnly(AssertablePublishedEvents events) {
        // Given
        var email = newEmail();
        issueCode(email);

        // When
        var registration = complete(request(email, CODE), Locale.of("en")).orElseThrow(AssertionError::new);

        // Then
        assertThat(events)
                .contains(PersonRegistered.class)
                .matching(PersonRegistered::aggregateId, registration.personId());
        assertThat(Arrays.stream(PersonRegistered.class.getRecordComponents()).map(component -> component.getName()))
                .containsExactly("eventId", "occurredAt", "aggregateId", "aggregateVersion", "eventVersion");
        assertThat(events)
                .contains(RecoveryCodesIssued.class)
                .matching(RecoveryCodesIssued::reason, RecoveryCodesIssued.Reason.SIGN_UP);
    }

    @Test
    void aWrongCodeIsAnInvalidCodeAndRegistersNobody() {
        // Given
        var email = newEmail();
        issueCode(email);

        // When
        var result = complete(request(email, "654321"), Locale.of("en"));

        // Then
        assertThat(result).isEqualTo(Result.failure(new IdentityRefusal.InvalidCode()));
        assertThat(people(email)).isZero();
    }

    @Test
    void aCodeThatWasNeverIssuedIsAnInvalidCode() {
        assertThat(complete(request(newEmail(), CODE), Locale.of("en")))
                .isEqualTo(Result.failure(new IdentityRefusal.InvalidCode()));
    }

    @Test
    void aUsedCodeIsAnInvalidCode() {
        // Given
        var email = newEmail();
        issueCode(email);
        complete(request(email, CODE), Locale.of("en")).orElseThrow(AssertionError::new);

        // When
        var result = complete(request(email, CODE), Locale.of("en"));

        // Then
        assertThat(result).isEqualTo(Result.failure(new IdentityRefusal.InvalidCode()));
        assertThat(people(email)).isOne();
    }

    @Test
    void afterFiveWrongCodesTheRightOneFailsToo() {
        // Given
        var email = newEmail();
        issueCode(email);
        for (var attempt = 1; attempt <= 5; attempt++) {
            complete(request(email, "000000"), Locale.of("en"));
        }

        // When
        var result = complete(request(email, CODE), Locale.of("en"));

        // Then
        assertThat(result).isEqualTo(Result.failure(new IdentityRefusal.InvalidCode()));
    }

    @Test
    void theEleventhCheckForOneAddressWithinFifteenMinutesIsTooManyAttempts() {
        // Given ten checks, each from another client address
        var email = newEmail();
        issueCode(email);
        for (var attempt = 1; attempt <= 10; attempt++) {
            assertThat(complete(request(email, "000000"), Locale.of("en")))
                    .isEqualTo(Result.failure(new IdentityRefusal.InvalidCode()));
        }

        // When
        var result = complete(request(email, CODE), Locale.of("en"));

        // Then
        assertThat(result).isEqualTo(Result.failure(new IdentityRefusal.TooManyAttempts()));
    }

    @Test
    void theFiftyFirstCheckFromOneClientAddressWithinFifteenMinutesIsTooManyAttempts() {
        // Given
        var clientAddress = newClientAddress();
        for (var attempt = 1; attempt <= 50; attempt++) {
            completeSignUp.complete(request(newEmail(), CODE), clientAddress, Locale.of("en"));
        }
        var email = newEmail();
        issueCode(email);

        // When
        var result = completeSignUp.complete(request(email, CODE), clientAddress, Locale.of("en"));

        // Then
        assertThat(result).isEqualTo(Result.failure(new IdentityRefusal.TooManyAttempts()));
        assertThat(complete(request(email, CODE), Locale.of("en"))).isInstanceOf(Result.Success.class);
    }

    @Test
    void aShortPasswordIsRejectedWithoutSpendingTheCode() {
        // Given
        var email = newEmail();
        issueCode(email);

        // When
        var result = complete(request(email, CODE, "fourteen-chars", "Ana", "2026-09-19"), Locale.of("en"));

        // Then
        assertThat(result)
                .isEqualTo(Result.failure(new IdentityRefusal.PasswordRefused(PasswordRejected.Reason.TOO_SHORT)));
        assertThat(complete(request(email, CODE), Locale.of("en"))).isInstanceOf(Result.Success.class);
    }

    @Test
    void aBreachedPasswordIsRejectedWithoutSpendingTheCode() {
        // Given
        var email = newEmail();
        issueCode(email);

        // When
        var result = complete(request(email, CODE, BREACHED_PASSWORD, "Ana", "2026-09-19"), Locale.of("en"));

        // Then
        assertThat(result)
                .isEqualTo(Result.failure(new IdentityRefusal.PasswordRefused(PasswordRejected.Reason.BREACHED)));
        assertThat(complete(request(email, CODE), Locale.of("en"))).isInstanceOf(Result.Success.class);
    }

    @Test
    void anEmptyNameIsAnInvalidNameWithoutSpendingTheCode() {
        // Given
        var email = newEmail();
        issueCode(email);

        // When
        var result = complete(request(email, CODE, PASSWORD, "  ", "2026-09-19"), Locale.of("en"));

        // Then
        assertThat(result).isEqualTo(Result.failure(new IdentityRefusal.InvalidName()));
        assertThat(complete(request(email, CODE), Locale.of("en"))).isInstanceOf(Result.Success.class);
    }

    @Test
    void aTermsVersionThatIsNotCurrentIsOutdatedWithoutSpendingTheCode() {
        // Given
        var email = newEmail();
        issueCode(email);

        // When
        var result = complete(request(email, CODE, PASSWORD, "Ana", "2020-01-01"), Locale.of("en"));

        // Then
        assertThat(result).isEqualTo(Result.failure(new IdentityRefusal.LegalTermsOutdated()));
        assertThat(complete(request(email, CODE), Locale.of("en"))).isInstanceOf(Result.Success.class);
    }

    @Test
    void aCompletionForAnAddressThatWasRegisteredMeanwhileIsAlreadyRegistered() {
        // Given a code for the address in another case, then the address registered
        var email = newEmail();
        issueCode(email);
        complete(request(email, CODE), Locale.of("en")).orElseThrow(AssertionError::new);
        issueCode(email.toUpperCase(Locale.ROOT));

        // When
        var result = complete(request(email.toUpperCase(Locale.ROOT), CODE), Locale.of("en"));

        // Then
        assertThat(result).isEqualTo(Result.failure(new IdentityRefusal.EmailAlreadyRegistered()));
        assertThat(people(email)).isOne();
    }

    @Test
    void aStartForARegisteredAddressStoresNoCodeAndSendsOneAccountExistsNotice() {
        // Given
        var email = newEmail();
        issueCode(email);
        complete(request(email, CODE), Locale.of("en")).orElseThrow(AssertionError::new);

        // When
        var result = startSignUp.start(new EmailAddress(email), newClientAddress(), Locale.of("es"));

        // Then
        assertThat(result).isEqualTo(Result.success(StartSignUp.Outcome.STARTED));
        await().atMost(MAIL_WAIT).until(() -> accountExistsMailsTo(email).size() == 1);
        var notice = accountExistsMailsTo(email).getFirst();
        assertThat(notice.locale()).isEqualTo(Locale.of("es"));
        assertThat(codeMailsTo(email)).isEmpty();
        assertThat(valkey.hasKey("frappe:secret:identity:sign-up:" + subjectOf(email)))
                .isFalse();
    }

    @Test
    void theSixthStartForARegisteredAddressWithinAnHourSendsNothing() {
        // Given a registered address and five starts, each noticed
        var email = newEmail();
        issueCode(email);
        complete(request(email, CODE), Locale.of("en")).orElseThrow(AssertionError::new);
        for (var start = 1; start <= 5; start++) {
            startSignUp.start(new EmailAddress(email), newClientAddress(), Locale.of("en"));
            var sent = start;
            await().atMost(MAIL_WAIT).until(() -> accountExistsMailsTo(email).size() == sent);
        }

        // When
        var result = startSignUp.start(new EmailAddress(email), newClientAddress(), Locale.of("en"));

        // Then
        assertThat(result).isEqualTo(Result.success(StartSignUp.Outcome.CAPPED));
        await().during(Duration.ofSeconds(1))
                .atMost(Duration.ofSeconds(3))
                .until(() -> accountExistsMailsTo(email).size() == 5);
    }

    @Test
    void aCompletionCountsOneRegisteredPersonAndOneSignUpIssueOfRecoveryCodes() {
        // Given
        var email = newEmail();
        issueCode(email);
        var registered = counted("frappe.identity.people.registered");
        var issued = countedSignUpIssues();

        // When
        complete(request(email, CODE), Locale.of("en")).orElseThrow(AssertionError::new);

        // Then
        assertThatBusinessMetric(meters, "frappe.identity.people.registered").hasCount(registered + 1);
        assertThatBusinessMetric(meters, "frappe.identity.recovery_codes.issued")
                .withTag("reason", "sign_up")
                .hasCount(issued + 1);
    }

    @Test
    void theRegistrationMetricsAreValid() {
        assertValidBusinessMetrics(PersonRegistered.class, RecoveryCodesIssued.class);
    }

    static CompleteSignUp.Request request(String email, String code) {
        return request(email, code, PASSWORD, "Ana", "2026-09-19");
    }

    static CompleteSignUp.Request request(
            String email, String code, String password, String givenName, String termsVersion) {
        return new CompleteSignUp.Request(
                new EmailAddress(email), code, password, givenName, "Pérez", termsVersion, "2026-09-19");
    }

    private Result<CompleteSignUp.Registration, IdentityRefusal> complete(
            CompleteSignUp.Request request, Locale locale) {
        return completeSignUp.complete(request, newClientAddress(), locale);
    }

    private void issueCode(String email) {
        secrets.put(SecretKey.of(IdentitySecrets.SIGN_UP, subjectOf(email)), CODE);
    }

    private long counted(String name) {
        var counter = meters.find(name).counter();
        return counter == null ? 0 : (long) counter.count();
    }

    private long countedSignUpIssues() {
        var counter = meters.find("frappe.identity.recovery_codes.issued")
                .tag("reason", "sign_up")
                .counter();
        return counter == null ? 0 : (long) counter.count();
    }

    private int people(String email) {
        var rows = jdbc.queryForObject(
                "select count(*) from identity.person where lower(email) = lower(?)", Integer.class, email);
        return rows == null ? 0 : rows;
    }

    private List<CodeMail> codeMailsTo(String email) {
        return mails.codeMails().stream()
                .filter(mail -> mail.recipient().equalsIgnoreCase(email))
                .toList();
    }

    private List<AccountExistsMail> accountExistsMailsTo(String email) {
        return mails.accountExistsMails().stream()
                .filter(mail -> mail.recipient().equalsIgnoreCase(email))
                .toList();
    }

    private UUID subjectOf(String email) {
        return digests.subjectOf("identity.email", email.toLowerCase(Locale.ROOT));
    }

    private static String newEmail() {
        return "ana-" + UUID.randomUUID() + "@example.com";
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
