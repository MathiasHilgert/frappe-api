package com.frappe.identity.infrastructure;

import static org.assertj.core.api.Assertions.assertThat;

import com.frappe.identity.domain.Password;
import org.junit.jupiter.api.Test;

class Argon2PasswordHasherTest {

    private final Argon2PasswordHasher hasher = new Argon2PasswordHasher();

    @Test
    void aHashVerifiesItsOwnPassword() {
        // Given
        var hash = hasher.hash(password("correct horse battery staple"));

        // When
        var verified = hasher.verify(password("correct horse battery staple"), hash);

        // Then
        assertThat(verified).isTrue();
    }

    @Test
    void aHashDoesNotVerifyAnotherPassword() {
        // Given
        var hash = hasher.hash(password("correct horse battery staple"));

        // When
        var verified = hasher.verify(password("correct horse battery stapler"), hash);

        // Then
        assertThat(verified).isFalse();
    }

    @Test
    void aPasswordTypedInFullwidthLettersVerifiesAgainstItsAsciiTwin() {
        // Given
        var hash = hasher.hash(password("ｃｏｒｒｅｃｔｈｏｒｓｅｂａｔ"));

        // When
        var verified = hasher.verify(password("correcthorsebat"), hash);

        // Then
        assertThat(verified).isTrue();
    }

    @Test
    void hashesAreArgon2idAndSalted() {
        // When
        var first = hasher.hash(password("correct horse battery staple"));
        var second = hasher.hash(password("correct horse battery staple"));

        // Then
        assertThat(first.value()).startsWith("$argon2id$");
        assertThat(first).isNotEqualTo(second);
    }

    @Test
    void theDummyVerificationRunsTheSameArgon2Parameters() {
        // Given
        var real = hasher.hash(password("correct horse battery staple"));

        // When
        var dummy = hasher.dummyHash();

        // Then: "$argon2id$v=19$m=…,t=…,p=…$salt$hash"; everything before the salt is the parameters
        assertThat(parametersOf(dummy.value())).isEqualTo(parametersOf(real.value()));
        hasher.verifyDummy(password("correct horse battery staple"));
    }

    @Test
    void hashesUseTheOwaspBaselineParameters() {
        // When
        var encoded = hasher.hash(password("correct horse battery staple")).value();

        // Then: m=19 MiB, t=2, p=1; base64 without padding: 16-byte salt = 22, 32-byte hash = 43 characters
        assertThat(encoded).matches("\\$argon2id\\$v=19\\$m=19456,t=2,p=1\\$[A-Za-z0-9+/]{22}\\$[A-Za-z0-9+/]{43}");
        assertThat(parametersOf(hasher.dummyHash().value())).isEqualTo("$argon2id$v=19$m=19456,t=2,p=1");
    }

    private static String parametersOf(String encoded) {
        return encoded.substring(0, encoded.lastIndexOf('$', encoded.lastIndexOf('$') - 1));
    }

    private static Password password(String raw) {
        return Password.of(raw).orElseThrow(rejected -> new AssertionError("rejected: " + rejected));
    }
}
