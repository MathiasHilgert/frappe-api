package com.frappe.identity.domain;

import static org.assertj.core.api.Assertions.assertThat;

import com.frappe.identity.domain.PasswordRejected.Reason;
import com.frappe.platform.Result;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;

class PasswordTest {

    @Test
    void fourteenCodePointsAreTooShort() {
        // When
        var result = Password.of("a".repeat(14));

        // Then
        assertThat(result).isEqualTo(Result.failure(new PasswordRejected(Reason.TOO_SHORT)));
    }

    @ParameterizedTest
    @ValueSource(ints = {15, 128})
    void lengthsFromFifteenToOneHundredTwentyEightCodePointsAreAccepted(int length) {
        // When
        var result = Password.of("a".repeat(length));

        // Then
        assertThat(result).isInstanceOf(Result.Success.class);
    }

    @Test
    void oneHundredTwentyNineCodePointsAreTooLong() {
        // When
        var result = Password.of("a".repeat(129));

        // Then
        assertThat(result).isEqualTo(Result.failure(new PasswordRejected(Reason.TOO_LONG)));
    }

    @Test
    void lengthCountsCodePointsNotUtf16Units() {
        // Given: 15 emoji, each two UTF-16 units
        var raw = "😀".repeat(15);

        // When
        var result = Password.of(raw);

        // Then
        assertThat(result).isInstanceOf(Result.Success.class);
    }

    @Test
    void lengthIsCountedAfterNfkcNormalization() {
        // Given: 14 "ﬁ" ligatures, which NFKC expands to 28 code points
        var raw = "ﬁ".repeat(14);

        // When
        var result = Password.of(raw);

        // Then
        assertThat(result).isInstanceOf(Result.Success.class);
    }

    @Test
    void fullwidthLettersNormalizeToTheirAsciiTwin() {
        // Given
        var fullwidth = "ｃｏｒｒｅｃｔｈｏｒｓｅｂａｔ";

        // When
        var normalized = Password.of(fullwidth);

        // Then
        assertThat(normalized).isEqualTo(Password.of("correcthorsebat"));
    }

    @Test
    void noCompositionRulesApply() {
        // When
        var result = Password.of("aaaaaaaaaaaaaaa");

        // Then
        assertThat(result).isInstanceOf(Result.Success.class);
    }

    @Test
    void printingAPasswordNeverShowsIt() {
        // Given
        var password = accepted("correct horse battery staple");

        // When
        var printed = password.toString();

        // Then
        assertThat(printed).doesNotContain("horse");
    }

    static Password accepted(String raw) {
        return switch (Password.of(raw)) {
            case Result.Success<Password, PasswordRejected>(var password) -> password;
            case Result.Failure<Password, PasswordRejected>(var rejected) ->
                throw new AssertionError("rejected: " + rejected);
        };
    }
}
