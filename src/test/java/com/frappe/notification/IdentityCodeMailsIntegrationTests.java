package com.frappe.notification;

import static org.assertj.core.api.Assertions.assertThat;
import static org.awaitility.Awaitility.await;

import ch.qos.logback.classic.Logger;
import ch.qos.logback.classic.spi.ILoggingEvent;
import ch.qos.logback.core.read.ListAppender;
import com.frappe.CapturedLogs;
import com.frappe.TestMailpitConfiguration;
import com.frappe.TestMailpitConfiguration.MailpitContainer;
import com.frappe.TestNatsConfiguration;
import com.frappe.TestcontainersConfiguration;
import com.frappe.identity.AccountExistsMail;
import com.frappe.identity.CodeMail;
import com.frappe.identity.CodeMailer;
import com.frappe.identity.CodePurpose;
import io.micrometer.core.instrument.Counter;
import io.micrometer.core.instrument.MeterRegistry;
import java.time.Duration;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Optional;
import java.util.UUID;
import java.util.concurrent.CopyOnWriteArrayList;
import java.util.stream.Stream;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.Arguments;
import org.junit.jupiter.params.provider.MethodSource;
import org.slf4j.LoggerFactory;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.annotation.Import;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.web.client.RestClient;
import tools.jackson.databind.JsonNode;

/**
 * identity's codes rendered from the real templates and catalogs and delivered to Mailpit over SMTP (the {@code local}
 * profile's setup): one language per mail, English as the whole-message fallback.
 */
@SpringBootTest
@ActiveProfiles("local")
@Import({TestcontainersConfiguration.class, TestNatsConfiguration.class, TestMailpitConfiguration.class})
class IdentityCodeMailsIntegrationTests {

    private static final String CODE = "482913";

    private static final List<String> LANGUAGES = List.of("en", "es", "pt");

    // Explicit, so a renamed template or a changed naming rule fails the tests.
    private static final Map<CodePurpose, String> TEMPLATES = Map.of(
            CodePurpose.SIGN_UP, "notification/sign-up-code",
            CodePurpose.EMAIL_CHANGE, "notification/email-change-code",
            CodePurpose.PASSWORD_RESET, "notification/password-reset-code",
            CodePurpose.ACCOUNT_RECOVERY, "notification/account-recovery-code");

    @Autowired
    CodeMailer codeMailer;

    @Autowired
    MailpitContainer mailpit;

    @Autowired
    MeterRegistry meters;

    private final Logger root = (Logger) LoggerFactory.getLogger(org.slf4j.Logger.ROOT_LOGGER_NAME);

    private final List<String> recipients = new CopyOnWriteArrayList<>();

    private ListAppender<ILoggingEvent> logs;

    @BeforeEach
    void captureLogs() {
        // CodeMailer and the SMTP Mailer deliver synchronously on the calling thread, so capturing it sees every line.
        logs = CapturedLogs.fromCurrentThread();
        root.addAppender(logs);
    }

    @AfterEach
    void noCodeOrAddressInTheLogs() {
        root.detachAppender(logs);
        var lines = logs.list.stream()
                .flatMap(event -> Stream.concat(
                        Stream.of(event.getFormattedMessage()),
                        Optional.ofNullable(event.getKeyValuePairs()).orElse(List.of()).stream()
                                .map(pair -> String.valueOf(pair.value))))
                .toList();
        assertThat(lines).noneMatch(line -> line.contains(CODE));
        assertThat(lines).noneMatch(line -> recipients.stream().anyMatch(line::contains));
    }

    @Test
    void aSpanishSignUpCodeArrivesEntirelyInSpanishWithTheCatalogsSubject() {
        // Given
        var recipient = recipient();

        // When
        codeMailer.send(new CodeMail(recipient, Locale.of("es"), CodePurpose.SIGN_UP, CODE, Duration.ofMinutes(15)));

        // Then
        var mail = mailTo(recipient);
        assertThat(mail.get("Subject").asString()).isEqualTo("Tu código para crear tu cuenta de Frappé");
        assertThat(mail.get("HTML").asString())
                .contains("lang=\"es\"", CODE, "15 minutos")
                .doesNotContain("Your code", "minutes");
    }

    @Test
    void anUnsupportedLanguageRendersInEnglishAndCountsTheFallback() {
        // Given
        var recipient = recipient();
        var before = fallbacks("notification/password-reset-code");

        // When
        codeMailer.send(
                new CodeMail(recipient, Locale.FRENCH, CodePurpose.PASSWORD_RESET, CODE, Duration.ofMinutes(15)));

        // Then
        var mail = mailTo(recipient);
        assertThat(mail.get("HTML").asString()).contains("lang=\"en\"", CODE, "15 minutes");
        assertThat(fallbacks("notification/password-reset-code")).isEqualTo(before + 1);
    }

    @ParameterizedTest
    @MethodSource("purposesAndLanguages")
    void everyCodeMailHasATemplateAndEveryKeyInEachLanguage(CodePurpose purpose, String language) {
        // Given
        var recipient = recipient();
        var template = TEMPLATES.get(purpose);
        var before = fallbacks(template);

        // When
        codeMailer.send(new CodeMail(recipient, Locale.of(language), purpose, CODE, Duration.ofMinutes(15)));

        // Then no key was missing, so nothing fell back
        var mail = mailTo(recipient);
        assertThat(mail.get("HTML").asString()).contains("lang=\"" + language + "\"", CODE);
        assertThat(mail.get("Subject").asString()).isNotBlank().doesNotContain("notification.");
        assertThat(fallbacks(template)).isEqualTo(before);
    }

    @ParameterizedTest
    @MethodSource("languages")
    void theAccountExistsNoticeHasEveryKeyInEachLanguageAndNoCode(String language) {
        // Given
        var recipient = recipient();
        var before = fallbacks("notification/account-exists");

        // When
        codeMailer.sendAccountExists(new AccountExistsMail(recipient, Locale.of(language), UUID.randomUUID()));

        // Then
        var mail = mailTo(recipient);
        assertThat(mail.get("HTML").asString()).contains("lang=\"" + language + "\"");
        assertThat(mail.get("Subject").asString()).isNotBlank().doesNotContain("notification.");
        assertThat(fallbacks("notification/account-exists")).isEqualTo(before);
    }

    static Stream<Arguments> purposesAndLanguages() {
        assertThat(TEMPLATES).containsOnlyKeys(CodePurpose.values());
        return Stream.of(CodePurpose.values())
                .flatMap(purpose -> LANGUAGES.stream().map(language -> Arguments.of(purpose, language)));
    }

    static Stream<String> languages() {
        return LANGUAGES.stream();
    }

    private String recipient() {
        var recipient = "ana-" + UUID.randomUUID() + "@" + TestMailpitConfiguration.ACCEPTED_DOMAIN;
        recipients.add(recipient);
        return recipient;
    }

    private double fallbacks(String template) {
        return meters.find("mail.locale.fallback").tag("mail.template", template).counters().stream()
                .mapToDouble(Counter::count)
                .sum();
    }

    private JsonNode mailTo(String recipient) {
        var api = RestClient.create(mailpit.apiUrl());
        var summary = await().atMost(Duration.ofSeconds(10))
                .until(() -> firstMessageTo(api, recipient), node -> node != null);
        return api.get()
                .uri("/api/v1/message/{id}", summary.get("ID").asString())
                .retrieve()
                .body(JsonNode.class);
    }

    private static JsonNode firstMessageTo(RestClient api, String recipient) {
        var found = api.get()
                .uri(uri -> uri.path("/api/v1/search")
                        .queryParam("query", "to:" + recipient)
                        .build())
                .retrieve()
                .body(JsonNode.class);
        var messages = found.get("messages");
        return messages.isEmpty() ? null : messages.get(0);
    }
}
