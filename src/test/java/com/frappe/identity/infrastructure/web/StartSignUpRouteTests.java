package com.frappe.identity.infrastructure.web;

import static org.assertj.core.api.Assertions.assertThat;
import static org.awaitility.Awaitility.await;

import com.frappe.TestNatsConfiguration;
import com.frappe.TestValkeyConfiguration;
import com.frappe.TestcontainersConfiguration;
import com.frappe.identity.CodeMail;
import com.frappe.identity.CodePurpose;
import com.frappe.identity.RecordingCodeMailer;
import com.frappe.platform.KeyedDigests;
import java.time.Duration;
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
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.web.servlet.assertj.MockMvcTester;
import org.springframework.test.web.servlet.assertj.MvcTestResult;
import tools.jackson.databind.json.JsonMapper;

/** {@code POST /v1/sign-ups}: the HTTP contract of starting a sign-up, on real Postgres and Valkey. */
@SpringBootTest
@AutoConfigureMockMvc
@Import({
    TestcontainersConfiguration.class,
    TestValkeyConfiguration.class,
    TestNatsConfiguration.class,
    StartSignUpRouteTests.Mails.class
})
@ActiveProfiles("local")
class StartSignUpRouteTests {

    private static final String PATH = "/v1/sign-ups";

    @TestConfiguration(proxyBeanMethods = false)
    static class Mails {

        @Bean
        @Primary
        RecordingCodeMailer recordingCodeMailer() {
            return new RecordingCodeMailer();
        }
    }

    @Autowired
    MockMvcTester http;

    @Autowired
    JsonMapper json;

    @Autowired
    RecordingCodeMailer mails;

    @Autowired
    KeyedDigests digests;

    @Autowired
    JdbcTemplate jdbc;

    @Test
    void aNewAddressIsAcceptedWithoutABodyAndMailedACodeInTheRequestsLanguage() {
        // Given
        var email = newEmail();

        // When
        var result = signUp(email, newClientAddress(), "es");

        // Then
        assertThat(result).hasStatus(HttpStatus.ACCEPTED);
        assertThat(result.getResponse().getContentAsByteArray()).isEmpty();
        assertThat(signUpRows(email)).isOne();
        await().atMost(Duration.ofSeconds(10)).until(() -> codeMailsTo(email).size() == 1);
        var mail = codeMailsTo(email).getFirst();
        assertThat(mail.purpose()).isEqualTo(CodePurpose.SIGN_UP);
        assertThat(mail.locale()).isEqualTo(Locale.of("es"));
        assertThat(mail.code()).matches("\\d{6}");
    }

    @Test
    void theSixthStartWithinAnHourIsAcceptedAlikeButMailsNothing() {
        // Given
        var email = newEmail();
        for (var start = 1; start <= 5; start++) {
            assertThat(signUp(email, newClientAddress(), "en")).hasStatus(HttpStatus.ACCEPTED);
        }
        await().atMost(Duration.ofSeconds(10)).until(() -> codeMailsTo(email).size() == 5);

        // When
        var result = signUp(email, newClientAddress(), "en");

        // Then
        assertThat(result).hasStatus(HttpStatus.ACCEPTED);
        assertThat(result.getResponse().getContentAsByteArray()).isEmpty();
        await().during(Duration.ofSeconds(1))
                .atMost(Duration.ofSeconds(3))
                .until(() -> codeMailsTo(email).size() == 5);
    }

    @ParameterizedTest(name = "{0}")
    @CsvSource({
        "en, Too many attempts, Wait a few minutes before trying again.",
        "es, Demasiados intentos, Espera unos minutos antes de volver a intentarlo."
    })
    void theTwentyFirstStartFromOneClientAddressIsTooManyAttempts(String language, String title, String detail) {
        // Given
        var clientAddress = newClientAddress();
        for (var start = 1; start <= 20; start++) {
            assertThat(signUp(newEmail(), clientAddress, language)).hasStatus(HttpStatus.ACCEPTED);
        }

        // When
        var result = signUp(newEmail(), clientAddress, language);

        // Then
        assertThat(result)
                .hasStatus(HttpStatus.TOO_MANY_REQUESTS)
                .hasContentType(MediaType.APPLICATION_PROBLEM_JSON)
                .hasHeader(HttpHeaders.CONTENT_LANGUAGE, language);
        assertThat(problemOf(result))
                .containsEntry("type", "https://frappe.app/problems/too-many-attempts")
                .containsEntry("code", "too-many-attempts")
                .containsEntry("status", 429)
                .containsEntry("title", title)
                .containsEntry("detail", detail)
                .containsEntry("params", Map.of());
    }

    @Test
    void aMissingAddressIsAnInvalidRequest() {
        // When
        var result = post("{}", newClientAddress());

        // Then
        assertThat(result).hasStatus(HttpStatus.BAD_REQUEST);
        assertThat(problemOf(result)).containsEntry("code", "invalid-request");
        assertThat(errorsOf(result))
                .singleElement()
                .satisfies(error ->
                        assertThat(error).containsEntry("pointer", "/email").containsEntry("code", "not-null"));
    }

    @Test
    void anAddressWithoutOneAtSignIsAnInvalidRequest() {
        // When
        var result = post("{\"email\": \"ana.example.com\"}", newClientAddress());

        // Then
        assertThat(result).hasStatus(HttpStatus.BAD_REQUEST);
        assertThat(errorsOf(result))
                .singleElement()
                .satisfies(error ->
                        assertThat(error).containsEntry("pointer", "/email").containsEntry("code", "email"));
    }

    private MvcTestResult signUp(String email, String clientAddress, String language) {
        return http.post()
                .uri(PATH)
                .contentType(MediaType.APPLICATION_JSON)
                .header(HttpHeaders.ACCEPT_LANGUAGE, language)
                .content("{\"email\": \"%s\"}".formatted(email))
                .with(request -> {
                    request.setRemoteAddr(clientAddress);
                    return request;
                })
                .exchange();
    }

    private MvcTestResult post(String body, String clientAddress) {
        return http.post()
                .uri(PATH)
                .contentType(MediaType.APPLICATION_JSON)
                .content(body)
                .with(request -> {
                    request.setRemoteAddr(clientAddress);
                    return request;
                })
                .exchange();
    }

    @SuppressWarnings("unchecked")
    private Map<String, Object> problemOf(MvcTestResult result) {
        return json.readValue(result.getResponse().getContentAsByteArray(), Map.class);
    }

    @SuppressWarnings("unchecked")
    private List<Map<String, Object>> errorsOf(MvcTestResult result) {
        return (List<Map<String, Object>>) problemOf(result).get("errors");
    }

    private List<CodeMail> codeMailsTo(String email) {
        return mails.codeMails().stream()
                .filter(mail -> mail.recipient().equals(email))
                .toList();
    }

    private int signUpRows(String email) {
        var subject = digests.subjectOf("identity.email", email.toLowerCase(Locale.ROOT));
        var rows = jdbc.queryForObject(
                "select count(*) from identity.sign_up where email_subject = ?", Integer.class, subject);
        return rows == null ? 0 : rows;
    }

    private static String newEmail() {
        return "ana-" + UUID.randomUUID() + "@example.com";
    }

    // Documentation range 198.51.100.0/24 is too small for fresh addresses per test; 10/8 is private.
    private static String newClientAddress() {
        var random = ThreadLocalRandom.current();
        return "10.%d.%d.%d".formatted(random.nextInt(256), random.nextInt(256), random.nextInt(1, 255));
    }
}
