package com.frappe.platform.mail;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatIllegalArgumentException;
import static org.assertj.core.api.Assertions.assertThatNullPointerException;

import java.util.HashMap;
import java.util.Locale;
import java.util.Map;
import java.util.Optional;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;

class MailMessageTest {

    private static final Locale SPANISH = Locale.of("es");

    private static final Locale ENGLISH = Locale.of("en");

    @Test
    void holdsTheTemplateRecipientLocalesAndModel() {
        // When
        var message =
                MailMessage.of("identity/email-proof", "ana@example.com", SPANISH, ENGLISH, Map.of("code", "123456"));

        // Then
        assertThat(message.templateId()).isEqualTo("identity/email-proof");
        assertThat(message.recipient()).isEqualTo("ana@example.com");
        assertThat(message.locale()).isEqualTo(SPANISH);
        assertThat(message.fallbackLocale()).isEqualTo(ENGLISH);
        assertThat(message.model()).containsEntry("code", "123456");
        assertThat(message.idempotencyKey()).isEmpty();
    }

    @Test
    void carriesAnIdempotencyKeyWhenGiven() {
        // When
        var message = MailMessage.of("identity/email-proof", "ana@example.com", SPANISH, ENGLISH, Map.of())
                .withIdempotencyKey("email-proof/01996a4e-0000-7000-8000-000000000001");

        // Then
        assertThat(message.idempotencyKey()).contains("email-proof/01996a4e-0000-7000-8000-000000000001");
    }

    @ParameterizedTest
    @ValueSource(strings = {"", "identity", "Identity/email-proof", "identity/Email", "identity/email proof", "/x"})
    void rejectsTemplateIdsThatAreNotModuleSlashKebabName(String templateId) {
        assertThatIllegalArgumentException()
                .isThrownBy(() -> MailMessage.of(templateId, "ana@example.com", SPANISH, ENGLISH, Map.of()));
    }

    @ParameterizedTest
    @ValueSource(strings = {"", " ", "ana", "@example.com", "ana@"})
    void rejectsRecipientsThatAreNotAnAddress(String recipient) {
        assertThatIllegalArgumentException()
                .isThrownBy(() -> MailMessage.of("identity/email-proof", recipient, SPANISH, ENGLISH, Map.of()));
    }

    @Test
    void rejectsABlankOrOverlongIdempotencyKey() {
        // Given
        var message = MailMessage.of("identity/email-proof", "ana@example.com", SPANISH, ENGLISH, Map.of());

        // Then Resend accepts at most 256 characters
        assertThatIllegalArgumentException().isThrownBy(() -> message.withIdempotencyKey(" "));
        assertThatIllegalArgumentException().isThrownBy(() -> message.withIdempotencyKey("k".repeat(257)));
    }

    @Test
    void rejectsMissingParts() {
        assertThatNullPointerException()
                .isThrownBy(() -> MailMessage.of("identity/email-proof", "ana@example.com", null, ENGLISH, Map.of()));
        assertThatNullPointerException()
                .isThrownBy(() ->
                        new MailMessage("identity/email-proof", "ana@example.com", SPANISH, ENGLISH, Map.of(), null));
    }

    @Test
    void copiesTheModel() {
        // Given
        var model = new HashMap<String, Object>(Map.of("code", "123456"));
        var message = MailMessage.of("identity/email-proof", "ana@example.com", SPANISH, ENGLISH, model);

        // When
        model.put("code", "999999");

        // Then
        assertThat(message.model()).containsEntry("code", "123456");
    }

    @Test
    void neverPrintsTheFullRecipientOrTheModel() {
        // Given
        var message = MailMessage.of(
                        "identity/email-proof", "ana.maria@example.com", SPANISH, ENGLISH, Map.of("code", "123456"))
                .withIdempotencyKey("proof-1");

        // When
        var printed = message.toString();

        // Then
        assertThat(printed)
                .contains("identity/email-proof", "a***@example.com", "es")
                .doesNotContain("ana.maria", "123456");
        assertThat(message.maskedRecipient()).isEqualTo("a***@example.com");
        assertThat(message.idempotencyKey()).isEqualTo(Optional.of("proof-1"));
    }
}
