package com.frappe.identity.infrastructure.web;

import static org.assertj.core.api.Assertions.assertThat;

import com.frappe.TestNatsConfiguration;
import com.frappe.TestValkeyConfiguration;
import com.frappe.TestcontainersConfiguration;
import com.frappe.identity.IdentitySecrets;
import com.frappe.identity.RecordingCodeMailer;
import com.frappe.identity.domain.BreachStatus;
import com.frappe.identity.domain.BreachedPasswords;
import com.frappe.platform.KeyedDigests;
import com.frappe.platform.SecretKey;
import com.frappe.platform.ShortLivedSecretStore;
import java.nio.charset.StandardCharsets;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.UUID;
import java.util.concurrent.ThreadLocalRandom;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.CsvSource;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.test.context.TestConfiguration;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Import;
import org.springframework.context.annotation.Primary;
import org.springframework.http.HttpHeaders;
import org.springframework.http.HttpStatus;
import org.springframework.http.MediaType;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.web.servlet.assertj.MockMvcTester;
import org.springframework.test.web.servlet.assertj.MvcTestResult;
import tools.jackson.databind.json.JsonMapper;

/** {@code POST /v1/sign-ups/completion}: the HTTP contract of completing a sign-up, on real Postgres and Valkey. */
@SpringBootTest
@AutoConfigureMockMvc
@Import({
    TestcontainersConfiguration.class,
    TestValkeyConfiguration.class,
    TestNatsConfiguration.class,
    CompleteSignUpRouteTests.Doubles.class
})
@ActiveProfiles("local")
class CompleteSignUpRouteTests {

    private static final String PATH = "/v1/sign-ups/completion";

    private static final String CODE = "123456";

    private static final String PASSWORD = "a long enough passphrase 42";

    private static final String BREACHED_PASSWORD = "password1234567890";

    private static final String VERSION = "2026-09-19";

    @TestConfiguration(proxyBeanMethods = false)
    static class Doubles {

        @Bean
        @Primary
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

    @Autowired
    MockMvcTester http;

    @Autowired
    JsonMapper json;

    @Autowired
    ShortLivedSecretStore secrets;

    @Autowired
    KeyedDigests digests;

    @Test
    void theRightCodeCreatesThePersonAndReturnsEightRecoveryCodesOnce() {
        // Given
        var email = newEmail();
        issueCode(email);

        // When
        var result = complete(body(email, CODE, PASSWORD, "Ana", VERSION), "es");

        // Then
        assertThat(result)
                .hasStatus(HttpStatus.CREATED)
                .hasContentTypeCompatibleWith(MediaType.APPLICATION_JSON)
                .hasHeader(HttpHeaders.LOCATION, "/v1/me");
        var answer = bodyOf(result);
        assertThat(UUID.fromString((String) answer.get("personId"))).isNotNull();
        assertThat((List<?>) answer.get("recoveryCodes")).hasSize(8).doesNotHaveDuplicates();
        assertThat(answer).containsOnlyKeys("personId", "recoveryCodes");
    }

    @ParameterizedTest(name = "{0}")
    @CsvSource(
            delimiter = '|',
            value = {
                "en | Invalid code | The code is wrong or no longer valid. Request a new one.",
                "es | Código no válido | El código es incorrecto o ya no es válido. Solicita uno nuevo."
            })
    void aWrongCodeIsAnInvalidCode(String language, String title, String detail) {
        // Given
        var email = newEmail();
        issueCode(email);

        // When
        var result = complete(body(email, "654321", PASSWORD, "Ana", VERSION), language);

        // Then
        assertThat(result)
                .hasStatus(HttpStatus.BAD_REQUEST)
                .hasContentType(MediaType.APPLICATION_PROBLEM_JSON)
                .hasHeader(HttpHeaders.CONTENT_LANGUAGE, language);
        assertThat(bodyOf(result))
                .containsEntry("type", "https://frappe.app/problems/invalid-code")
                .containsEntry("code", "invalid-code")
                .containsEntry("title", title)
                .containsEntry("detail", detail)
                .containsEntry("params", Map.of());
    }

    @Test
    void anUnknownAddressIsTheSameInvalidCode() {
        // When
        var result = complete(body(newEmail(), CODE, PASSWORD, "Ana", VERSION), "en");

        // Then
        assertThat(result).hasStatus(HttpStatus.BAD_REQUEST);
        assertThat(bodyOf(result)).containsEntry("code", "invalid-code");
    }

    @Test
    void theEleventhAttemptForOneAddressIsTooManyAttempts() {
        // Given
        var email = newEmail();
        for (var attempt = 1; attempt <= 10; attempt++) {
            assertThat(complete(body(email, CODE, PASSWORD, "Ana", VERSION), "en"))
                    .hasStatus(HttpStatus.BAD_REQUEST);
        }
        issueCode(email);

        // When
        var result = complete(body(email, CODE, PASSWORD, "Ana", VERSION), "en");

        // Then
        assertThat(result).hasStatus(HttpStatus.TOO_MANY_REQUESTS);
        assertThat(bodyOf(result)).containsEntry("code", "too-many-attempts");
    }

    @ParameterizedTest(name = "{1}")
    @CsvSource({
        "fourteen-chars, too_short, Use at least 15 characters.",
        "password1234567890, breached, This password appeared in a data breach. Choose another one."
    })
    void aRejectedPasswordIsPasswordRejectedWithItsReasonAndTheCodeStillWorks(
            String password, String reason, String detail) {
        // Given
        var email = newEmail();
        issueCode(email);

        // When
        var result = complete(body(email, CODE, password, "Ana", VERSION), "en");

        // Then
        assertThat(result).hasStatus(HttpStatus.UNPROCESSABLE_CONTENT);
        assertThat(bodyOf(result))
                .containsEntry("code", "password-rejected")
                .containsEntry("params", Map.of("reason", reason))
                .containsEntry("detail", detail);
        assertThat(complete(body(email, CODE, PASSWORD, "Ana", VERSION), "en")).hasStatus(HttpStatus.CREATED);
    }

    @Test
    void aTermsVersionThatIsNotCurrentIsLegalTermsOutdated() {
        // Given
        var email = newEmail();
        issueCode(email);

        // When
        var result = complete(body(email, CODE, PASSWORD, "Ana", "2020-01-01"), "en");

        // Then
        assertThat(result).hasStatus(HttpStatus.CONFLICT);
        assertThat(bodyOf(result)).containsEntry("code", "legal-terms-outdated");
    }

    @Test
    void aNameOfEightyOneCharactersIsAnInvalidName() {
        // Given
        var email = newEmail();
        issueCode(email);

        // When
        var result = complete(body(email, CODE, PASSWORD, "a".repeat(81), VERSION), "en");

        // Then
        assertThat(result).hasStatus(HttpStatus.UNPROCESSABLE_CONTENT);
        assertThat(bodyOf(result)).containsEntry("code", "invalid-name");
    }

    @Test
    void aSecondPersonForTheSameAddressIsEmailAlreadyRegistered() {
        // Given
        var email = newEmail();
        issueCode(email);
        assertThat(complete(body(email, CODE, PASSWORD, "Ana", VERSION), "en")).hasStatus(HttpStatus.CREATED);
        issueCode(email);

        // When
        var result = complete(body(email, CODE, PASSWORD, "Ana", VERSION), "en");

        // Then
        assertThat(result).hasStatus(HttpStatus.CONFLICT);
        assertThat(bodyOf(result)).containsEntry("code", "email-already-registered");
    }

    @Test
    void aMissingPasswordIsAnInvalidRequest() {
        // When
        var result = complete("{\"email\": \"%s\", \"code\": \"%s\"}".formatted(newEmail(), CODE), "en");

        // Then
        assertThat(result).hasStatus(HttpStatus.BAD_REQUEST);
        assertThat(bodyOf(result)).containsEntry("code", "invalid-request");
    }

    @Test
    void theSpecDocumentsTheCompletion() {
        // When
        var spec = http.get().uri("/v3/api-docs").exchange();

        // Then
        assertThat(new String(spec.getResponse().getContentAsByteArray(), StandardCharsets.UTF_8))
                .contains("/v1/sign-ups/completion")
                .contains("legal-terms-outdated");
    }

    private MvcTestResult complete(String body, String language) {
        return http.post()
                .uri(PATH)
                .contentType(MediaType.APPLICATION_JSON)
                .header(HttpHeaders.ACCEPT_LANGUAGE, language)
                .content(body)
                .with(request -> {
                    request.setRemoteAddr(newClientAddress());
                    return request;
                })
                .exchange();
    }

    private static String body(String email, String code, String password, String givenName, String termsVersion) {
        return """
                {"email": "%s", "code": "%s", "password": "%s", "givenName": "%s", "familyName": "Pérez",
                 "termsVersion": "%s", "privacyVersion": "%s"}
                """.formatted(email, code, password, givenName, termsVersion, VERSION);
    }

    private void issueCode(String email) {
        var subject = digests.subjectOf("identity.email", email.toLowerCase(Locale.ROOT));
        secrets.put(SecretKey.of(IdentitySecrets.SIGN_UP, subject), CODE);
    }

    @SuppressWarnings("unchecked")
    private Map<String, Object> bodyOf(MvcTestResult result) {
        return json.readValue(result.getResponse().getContentAsByteArray(), Map.class);
    }

    private static String newEmail() {
        return "ana-" + UUID.randomUUID() + "@example.com";
    }

    private static String newClientAddress() {
        var random = ThreadLocalRandom.current();
        return "10.%d.%d.%d".formatted(random.nextInt(256), random.nextInt(256), random.nextInt(1, 255));
    }
}
