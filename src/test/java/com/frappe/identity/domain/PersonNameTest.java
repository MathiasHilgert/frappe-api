package com.frappe.identity.domain;

import static org.assertj.core.api.Assertions.assertThat;

import com.frappe.platform.Result;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;

class PersonNameTest {

    @Test
    void aNameIsTrimmedAndComposed() {
        // When: "José" with a combining acute accent, surrounded by spaces
        var name = PersonName.of("  José ");

        // Then
        assertThat(name).isEqualTo(Result.success(new PersonName("José")));
    }

    @ParameterizedTest
    @ValueSource(strings = {"", "   ", "\t"})
    void aBlankNameIsEmpty(String raw) {
        assertThat(PersonName.of(raw)).isEqualTo(Result.failure(NameRejected.EMPTY));
    }

    @Test
    void eightyCharactersAreAccepted() {
        assertThat(PersonName.of("a".repeat(80))).isEqualTo(Result.success(new PersonName("a".repeat(80))));
    }

    @Test
    void eightyOneCharactersAreTooLong() {
        assertThat(PersonName.of("a".repeat(81))).isEqualTo(Result.failure(NameRejected.TOO_LONG));
    }

    @Test
    void charactersAreCountedAsCodePoints() {
        // Given 80 emoji, each two UTF-16 units
        var raw = "😀".repeat(80);

        // Then
        assertThat(PersonName.of(raw)).isEqualTo(Result.success(new PersonName(raw)));
    }
}
