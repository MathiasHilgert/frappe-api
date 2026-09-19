package com.frappe.notification.infrastructure;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatExceptionOfType;

import ch.qos.logback.classic.Logger;
import ch.qos.logback.classic.spi.ILoggingEvent;
import ch.qos.logback.core.read.ListAppender;
import com.frappe.CapturedLogs;
import com.frappe.identity.AccountExistsMail;
import com.frappe.identity.CodeMail;
import com.frappe.identity.CodePurpose;
import com.frappe.platform.mail.MailDeliveryException;
import com.frappe.platform.mail.RecordingMailer;
import java.time.Duration;
import java.util.List;
import java.util.Locale;
import java.util.Optional;
import java.util.UUID;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.EnumSource;
import org.slf4j.LoggerFactory;

class IdentityCodeMailsTest {

    private static final String RECIPIENT = "ana@example.com";

    private static final String CODE = "482913";

    private final RecordingMailer mailer = new RecordingMailer();

    private final IdentityCodeMails mails = new IdentityCodeMails(mailer);

    private final Logger root = (Logger) LoggerFactory.getLogger(org.slf4j.Logger.ROOT_LOGGER_NAME);

    private ListAppender<ILoggingEvent> logs;

    @BeforeEach
    void captureLogs() {
        logs = CapturedLogs.fromCurrentThread();
        root.addAppender(logs);
    }

    @AfterEach
    void releaseLogs() {
        root.detachAppender(logs);
        assertThat(logs.list)
                .extracting(ILoggingEvent::getFormattedMessage)
                .noneMatch(line -> line.contains(CODE) || line.contains(RECIPIENT));
        assertThat(logs.list)
                .flatExtracting(
                        event -> Optional.ofNullable(event.getKeyValuePairs()).orElse(List.of()))
                .extracting(pair -> String.valueOf(pair.value))
                .noneMatch(value -> value.contains(CODE) || value.contains(RECIPIENT));
    }

    @Test
    void aSignUpCodeIsMailedInTheRecipientsLanguageWithTheCode() {
        // When
        mails.send(new CodeMail(RECIPIENT, Locale.of("es"), CodePurpose.SIGN_UP, CODE, Duration.ofMinutes(15)));

        // Then
        assertThat(mailer.sentTo(RECIPIENT)).singleElement().satisfies(message -> {
            assertThat(message.templateId()).isEqualTo("notification/sign-up-code");
            assertThat(message.locale()).isEqualTo(Locale.of("es"));
            assertThat(message.fallbackLocale()).isEqualTo(Locale.of("en"));
            assertThat(message.model()).containsEntry("code", CODE).containsEntry("minutes", 15L);
            assertThat(message.idempotencyKey()).isEmpty();
        });
    }

    @ParameterizedTest
    @EnumSource(CodePurpose.class)
    void eachPurposeHasItsOwnTemplate(CodePurpose purpose) {
        // When
        mails.send(new CodeMail(RECIPIENT, Locale.of("en"), purpose, CODE, Duration.ofMinutes(15)));

        // Then
        var expected = "notification/" + purpose.name().toLowerCase(Locale.ROOT).replace('_', '-') + "-code";
        assertThat(mailer.sent())
                .singleElement()
                .extracting(m -> m.templateId())
                .isEqualTo(expected);
    }

    @Test
    void theAccountExistsNoticeCarriesTheSameIdempotencyKeyOnEveryAttemptAndNoCode() {
        // Given
        var reference = UUID.randomUUID();
        var notice = new AccountExistsMail(RECIPIENT, Locale.of("pt"), reference);

        // When
        mails.sendAccountExists(notice);
        mails.sendAccountExists(notice);

        // Then
        assertThat(mailer.sentTo(RECIPIENT)).hasSize(2).allSatisfy(message -> {
            assertThat(message.templateId()).isEqualTo("notification/account-exists");
            assertThat(message.locale()).isEqualTo(Locale.of("pt"));
            assertThat(message.fallbackLocale()).isEqualTo(Locale.of("en"));
            assertThat(message.idempotencyKey()).contains("notification/account-exists/" + reference);
            assertThat(message.model()).doesNotContainKey("code");
        });
    }

    @Test
    void aTransientDeliveryFailureIsRethrownAndNotLoggedHere() {
        // Given
        mailer.failNextSend();
        var mail = new CodeMail(RECIPIENT, Locale.of("en"), CodePurpose.PASSWORD_RESET, CODE, Duration.ofMinutes(15));

        // When / Then
        assertThatExceptionOfType(MailDeliveryException.class).isThrownBy(() -> mails.send(mail));
        assertThat(logs.list).isEmpty();
    }
}
