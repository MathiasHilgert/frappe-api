package com.frappe.platform.mail;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatExceptionOfType;

import java.util.Locale;
import java.util.Map;
import org.junit.jupiter.api.Test;

class RecordingMailerTest {

    private final RecordingMailer mailer = new RecordingMailer();

    @Test
    void recordsEverySentMessageInOrder() {
        // When
        mailer.send(message("ana@example.com"));
        mailer.send(message("bruno@example.com"));

        // Then
        assertThat(mailer.sent())
                .extracting(MailMessage::recipient)
                .containsExactly("ana@example.com", "bruno@example.com");
        assertThat(mailer.sentTo("bruno@example.com"))
                .singleElement()
                .extracting(MailMessage::templateId)
                .isEqualTo("identity/email-proof");
    }

    @Test
    void failsTheNextSendLikeAnOutageAndRecordsNothing() {
        // Given
        mailer.failNextSend();

        // When / Then: the calling listener fails and the outbox would retry
        assertThatExceptionOfType(MailDeliveryException.class)
                .isThrownBy(() -> mailer.send(message("ana@example.com")));
        assertThat(mailer.sent()).isEmpty();

        // And the retry goes through
        mailer.send(message("ana@example.com"));
        assertThat(mailer.sent()).hasSize(1);
    }

    @Test
    void forgetsWhatWasSent() {
        // Given
        mailer.send(message("ana@example.com"));

        // When
        mailer.clear();

        // Then
        assertThat(mailer.sent()).isEmpty();
    }

    private static MailMessage message(String recipient) {
        return MailMessage.of("identity/email-proof", recipient, Locale.of("es"), Locale.of("en"), Map.of("code", "1"));
    }
}
