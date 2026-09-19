package com.frappe.platform.infrastructure.nats;

import static org.assertj.core.api.Assertions.assertThat;

import io.nats.client.NKey;
import java.nio.charset.StandardCharsets;
import java.security.SecureRandom;
import java.util.Collections;
import org.junit.jupiter.api.Test;

/**
 * Pins the BouncyCastle exclusion in build.gradle.kts: jnats' {@code bcprov-lts8on} is excluded in favour of the
 * {@code bcprov-jdk18on} Argon2 needs, so exactly one copy of each class is on the classpath, and NKey signing still
 * works with it.
 */
class BouncyCastleClasspathTest {

    @Test
    void exactlyOneBouncyCastleIsOnTheClasspath() throws Exception {
        // When
        var copies = Collections.list(
                getClass().getClassLoader().getResources("org/bouncycastle/crypto/signers/Ed25519Signer.class"));

        // Then
        assertThat(copies).singleElement().asString().contains("bcprov-jdk18on");
    }

    @Test
    void nkeysSignAndVerifyWithIt() throws Exception {
        // Given
        var user = NKey.createUser(new SecureRandom());
        var challenge = "nonce-from-the-server".getBytes(StandardCharsets.UTF_8);

        // When
        var signature = user.sign(challenge);

        // Then
        assertThat(user.verify(challenge, signature)).isTrue();
        assertThat(user.verify("another-nonce".getBytes(StandardCharsets.UTF_8), signature))
                .isFalse();
    }
}
