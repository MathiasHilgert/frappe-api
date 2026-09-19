package com.frappe.identity;

import static org.assertj.core.api.Assertions.assertThat;

import java.time.Duration;
import java.util.Locale;
import java.util.UUID;
import org.junit.jupiter.api.Test;

class CodeMailTest {

    @Test
    void printingACodeMailShowsNeitherTheCodeNorTheFullAddress() {
        // Given
        var mail = new CodeMail(
                "ana.silva@example.com",
                Locale.forLanguageTag("pt"),
                CodePurpose.SIGN_UP,
                "042917",
                Duration.ofMinutes(15));

        // When
        var printed = mail.toString();

        // Then
        assertThat(printed).doesNotContain("042917").doesNotContain("ana.silva").contains("SIGN_UP");
    }

    @Test
    void printingAnAccountExistsMailMasksTheRecipient() {
        // Given
        var mail = new AccountExistsMail(
                "ana.silva@example.com", Locale.ENGLISH, UUID.fromString("01996a4e-0000-7000-8000-000000000001"));

        // When
        var printed = mail.toString();

        // Then
        assertThat(printed).doesNotContain("ana.silva").contains("01996a4e-0000-7000-8000-000000000001");
    }

    @Test
    void aRecordingCodeMailerKeepsWhatWasSent() {
        // Given
        var mailer = new RecordingCodeMailer();
        var code = new CodeMail(
                "ana@example.com", Locale.ENGLISH, CodePurpose.PASSWORD_RESET, "123456", Duration.ofMinutes(15));
        var exists = new AccountExistsMail(
                "ana@example.com", Locale.ENGLISH, UUID.fromString("01996a4e-0000-7000-8000-000000000001"));

        // When
        mailer.send(code);
        mailer.sendAccountExists(exists);

        // Then
        assertThat(mailer.codeMails()).containsExactly(code);
        assertThat(mailer.accountExistsMails()).containsExactly(exists);
    }
}
