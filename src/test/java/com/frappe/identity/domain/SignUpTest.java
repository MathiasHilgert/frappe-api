package com.frappe.identity.domain;

import static org.assertj.core.api.Assertions.assertThat;

import java.time.Instant;
import java.util.Locale;
import java.util.UUID;
import org.junit.jupiter.api.Test;

class SignUpTest {

    private static final SignUpId ID = new SignUpId(UUID.fromString("01925f6e-0000-7000-8000-000000000001"));

    private static final UUID SUBJECT = UUID.fromString("01925f6e-0000-8000-8000-000000000002");

    private static final UUID EVENT = UUID.fromString("01925f6e-0000-7000-8000-000000000003");

    private static final Instant NOW = Instant.parse("2026-09-19T12:00:00Z");

    @Test
    void startingKeepsTheAddressAsEnteredItsSubjectAndLanguageAtVersionZero() {
        // When
        var signUp = SignUp.start(ID, email("Ana@Example.com"), SUBJECT, Locale.of("es"), NOW);

        // Then
        assertThat(signUp.id()).isEqualTo(ID);
        assertThat(signUp.email()).isEqualTo(email("Ana@Example.com"));
        assertThat(signUp.emailSubject()).isEqualTo(SUBJECT);
        assertThat(signUp.locale()).isEqualTo(Locale.of("es"));
        assertThat(signUp.startedAt()).isEqualTo(NOW);
        assertThat(signUp.version()).isZero();
    }

    @Test
    void theStartedEventNamesTheStoredIdAndVersionOnly() {
        // Given a start recorded over an earlier one
        var stored = SignUp.reconstitute(ID, email("ana@example.com"), SUBJECT, Locale.of("es"), NOW, 3);

        // When
        var event = stored.started(EVENT);

        // Then
        assertThat(event).isEqualTo(new SignUpStarted(EVENT, NOW, ID.value(), 3, SignUpStarted.VERSION));
    }

    private static EmailAddress email(String value) {
        return new EmailAddress(value);
    }
}
