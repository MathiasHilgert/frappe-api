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
    void startingKeepsTheAddressAsEnteredItsSubjectAndLanguage() {
        // When
        var signUp = SignUp.start(ID, email("Ana@Example.com"), SUBJECT, Locale.of("es"), NOW, EVENT);

        // Then
        assertThat(signUp.id()).isEqualTo(ID);
        assertThat(signUp.email()).isEqualTo(email("Ana@Example.com"));
        assertThat(signUp.emailSubject()).isEqualTo(SUBJECT);
        assertThat(signUp.locale()).isEqualTo(Locale.of("es"));
        assertThat(signUp.startedAt()).isEqualTo(NOW);
    }

    @Test
    void startingPublishesSignUpStartedWithTheIdOnly() {
        // Given
        var signUp = SignUp.start(ID, email("ana@example.com"), SUBJECT, Locale.of("es"), NOW, EVENT);

        // When
        var events = signUp.pullEvents();

        // Then
        assertThat(events).containsExactly(new SignUpStarted(EVENT, NOW, ID.value(), 0, SignUpStarted.VERSION));
        assertThat(signUp.pullEvents()).isEmpty();
    }

    @Test
    void restartingTakesTheLatestAddressAndLanguageAndPublishesTheNextVersion() {
        // Given
        var signUp = SignUp.reconstitute(ID, email("ana@example.com"), SUBJECT, Locale.of("es"), NOW, 3);
        var later = NOW.plusSeconds(60);
        var eventId = UUID.fromString("01925f6e-0000-7000-8000-000000000004");

        // When
        signUp.restart(email("ANA@example.com"), Locale.of("pt"), later, eventId);

        // Then
        assertThat(signUp.email()).isEqualTo(email("ANA@example.com"));
        assertThat(signUp.locale()).isEqualTo(Locale.of("pt"));
        assertThat(signUp.startedAt()).isEqualTo(later);
        assertThat(signUp.version()).isEqualTo(3);
        assertThat(signUp.pullEvents())
                .containsExactly(new SignUpStarted(eventId, later, ID.value(), 4, SignUpStarted.VERSION));
    }

    private static EmailAddress email(String value) {
        return new EmailAddress(value);
    }
}
