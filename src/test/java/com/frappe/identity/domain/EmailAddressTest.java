package com.frappe.identity.domain;

import static org.assertj.core.api.Assertions.assertThat;

import com.frappe.platform.Result;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;

class EmailAddressTest {

    @Test
    void anAddressIsTrimmed() {
        // When
        var result = EmailAddress.of("  Ana@Example.com \t");

        // Then
        assertThat(result.map(EmailAddress::value)).isEqualTo(Result.success("Ana@Example.com"));
    }

    @Test
    void theCanonicalFormIsLowercase() {
        // Given
        var address = accepted("Ana.SILVA@Example.COM");

        // When
        var canonical = address.canonical();

        // Then
        assertThat(canonical).isEqualTo("ana.silva@example.com");
    }

    @Test
    void theCanonicalFormIgnoresTheDefaultLocale() {
        // Given: a Turkish default locale would lowercase "I" to a dotless "ı"
        var address = accepted("IVAN@EXAMPLE.COM");

        // When
        var canonical = address.canonical();

        // Then
        assertThat(canonical).isEqualTo("ivan@example.com");
    }

    @Test
    void twoHundredFiftyFourCharactersAreAccepted() {
        // When
        var result = EmailAddress.of(addressOfLength(254));

        // Then
        assertThat(result).isInstanceOf(Result.Success.class);
    }

    @Test
    void twoHundredFiftyFiveCharactersAreTooLong() {
        // When
        var result = EmailAddress.of(addressOfLength(255));

        // Then
        assertThat(result).isEqualTo(Result.failure(EmailAddressRejected.TOO_LONG));
    }

    @ParameterizedTest
    @ValueSource(
            strings = {"", "   ", "ana.example.com", "ana@@example.com", "a@b@example.com", "@example.com", "ana@"})
    void anAddressWithoutExactlyOneAtBetweenTwoPartsIsMalformed(String raw) {
        // When
        var result = EmailAddress.of(raw);

        // Then
        assertThat(result).isEqualTo(Result.failure(EmailAddressRejected.MALFORMED));
    }

    @Test
    void printingAnAddressMasksIt() {
        // Given
        var address = accepted("ana.silva@example.com");

        // When
        var printed = address.toString();

        // Then
        assertThat(printed).doesNotContain("ana.silva");
    }

    private static String addressOfLength(int length) {
        var domain = "@example.com";
        return "a".repeat(length - domain.length()) + domain;
    }

    private static EmailAddress accepted(String raw) {
        return switch (EmailAddress.of(raw)) {
            case Result.Success<EmailAddress, EmailAddressRejected>(var address) -> address;
            case Result.Failure<EmailAddress, EmailAddressRejected>(var rejected) ->
                throw new AssertionError("rejected: " + rejected);
        };
    }
}
