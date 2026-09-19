package com.frappe.identity.infrastructure;

import static org.assertj.core.api.Assertions.assertThat;

import java.security.SecureRandom;
import java.util.HashSet;
import java.util.stream.IntStream;
import org.junit.jupiter.api.RepeatedTest;
import org.junit.jupiter.api.Test;

class SecureRandomSecretsTest {

    private final SecureRandomSecrets secrets = new SecureRandomSecrets(new SecureRandom());

    @RepeatedTest(20)
    void aTokenIsFortyThreeBase64UrlCharacters() {
        // When
        var token = secrets.token();

        // Then: 32 bytes, unpadded
        assertThat(token).matches("[A-Za-z0-9_-]{43}");
    }

    @RepeatedTest(20)
    void aCodeIsSixDigits() {
        // When
        var code = secrets.code();

        // Then
        assertThat(code).matches("\\d{6}");
    }

    @Test
    void aSmallCodeKeepsItsLeadingZeros() {
        // Given
        var random = new SecureRandom() {
            @Override
            public int nextInt(int bound) {
                return 42;
            }
        };

        // When
        var code = new SecureRandomSecrets(random).code();

        // Then
        assertThat(code).isEqualTo("000042");
    }

    @RepeatedTest(20)
    void aRecoveryCodeIsTenCrockfordBase32Characters() {
        // When
        var recoveryCode = secrets.recoveryCode();

        // Then: Crockford's alphabet has no I, L, O or U
        assertThat(recoveryCode).matches("[0-9ABCDEFGHJKMNPQRSTVWXYZ]{10}");
    }

    @Test
    void secretsDoNotRepeat() {
        // When
        var tokens = new HashSet<String>();
        IntStream.range(0, 100).forEach(i -> tokens.add(secrets.token()));

        // Then
        assertThat(tokens).hasSize(100);
    }
}
