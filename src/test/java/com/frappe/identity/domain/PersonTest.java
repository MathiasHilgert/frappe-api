package com.frappe.identity.domain;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatIllegalArgumentException;

import java.time.Clock;
import java.time.Instant;
import java.time.ZoneOffset;
import java.util.List;
import java.util.Locale;
import java.util.UUID;
import java.util.stream.IntStream;
import org.junit.jupiter.api.Test;

class PersonTest {

    private static final Clock CLOCK = Clock.fixed(Instant.parse("2026-09-19T12:00:00Z"), ZoneOffset.UTC);

    private static final PersonId ID = new PersonId(UUID.fromString("01925f6e-0000-7000-8000-000000000001"));

    private static final LegalAcceptance LEGAL = new LegalAcceptance("2026-09", "2026-08", CLOCK.instant());

    @Test
    void aRegisteredPersonIsActiveAtEpochOneWithEverythingItWasGiven() {
        // When
        var person = register(codes(8));

        // Then
        assertThat(person.id()).isEqualTo(ID);
        assertThat(person.status()).isEqualTo(PersonStatus.ACTIVE);
        assertThat(person.credentialEpoch()).isOne();
        assertThat(person.email()).isEqualTo(new EmailAddress("Ana@Example.com"));
        assertThat(person.givenName()).isEqualTo(new PersonName("Ana"));
        assertThat(person.familyName()).isEqualTo(new PersonName("Pérez"));
        assertThat(person.preferredLocale()).isEqualTo(Locale.of("es"));
        assertThat(person.legalAcceptance()).isEqualTo(LEGAL);
        assertThat(person.registeredAt()).isEqualTo(CLOCK.instant());
        assertThat(person.recoveryCodes()).hasSize(8);
        assertThat(person.version()).isZero();
    }

    @Test
    void aPersonIsRegisteredWithExactlyEightRecoveryCodes() {
        assertThatIllegalArgumentException().isThrownBy(() -> register(codes(7)));
    }

    private static Person register(List<RecoveryCode> codes) {
        return Person.register(
                ID,
                new EmailAddress("Ana@Example.com"),
                new PasswordHash("$argon2id$hash"),
                new PersonName("Ana"),
                new PersonName("Pérez"),
                Locale.of("es"),
                LEGAL,
                codes,
                CLOCK.instant());
    }

    private static List<RecoveryCode> codes(int count) {
        return IntStream.range(0, count)
                .mapToObj(i -> new RecoveryCode(UUID.randomUUID(), "digest-" + i))
                .toList();
    }
}
