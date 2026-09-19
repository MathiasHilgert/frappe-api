package com.frappe.platform.infrastructure.valkey;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatIllegalArgumentException;

import org.junit.jupiter.api.Test;
import org.springframework.security.crypto.argon2.Argon2PasswordEncoder;

class PepperedArgon2PasswordEncoderTest {

    private static final String PEPPER = "test-pepper-0123456789abcdefghijklmnop";

    private final PepperedArgon2PasswordEncoder encoder = new PepperedArgon2PasswordEncoder(PEPPER);

    @Test
    void matchesTheValueItHashed() {
        // When
        var hash = encoder.encode("493817");

        // Then
        assertThat(hash).startsWith("$argon2id$").doesNotContain("493817");
        assertThat(encoder.matches("493817", hash)).isTrue();
        assertThat(encoder.matches("493818", hash)).isFalse();
    }

    @Test
    void aStolenHashCannotBeCheckedWithoutThePepper() {
        // Given
        var hash = encoder.encode("493817");

        // Then plain Argon2 (an offline attacker with a dump) and another pepper both fail
        assertThat(Argon2PasswordEncoder.defaultsForSpringSecurity_v5_8().matches("493817", hash))
                .isFalse();
        assertThat(new PepperedArgon2PasswordEncoder("other-pepper-0123456789abcdefghijklmnop").matches("493817", hash))
                .isFalse();
    }

    @Test
    void rejectsAPepperShorterThan32Characters() {
        assertThatIllegalArgumentException()
                .isThrownBy(() -> new PepperedArgon2PasswordEncoder("too-short"))
                .withMessageContaining("FRAPPE_SECRET_PEPPER")
                .withMessageNotContaining("too-short");
    }
}
