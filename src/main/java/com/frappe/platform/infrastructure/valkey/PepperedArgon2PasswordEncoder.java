package com.frappe.platform.infrastructure.valkey;

import java.nio.charset.StandardCharsets;
import java.security.GeneralSecurityException;
import java.util.HexFormat;
import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;
import org.jspecify.annotations.Nullable;
import org.springframework.security.crypto.argon2.Argon2PasswordEncoder;
import org.springframework.security.crypto.password.PasswordEncoder;

/**
 * Argon2id (Spring Security's v5.8 defaults) over HMAC-SHA256 of the secret with a server-side pepper. Codes are short
 * (a 6-digit code has a million values), so a salted hash alone falls to an offline brute force of a leaked Valkey
 * dump; without the pepper, which never reaches Valkey, the dump is useless.
 *
 * <p>Changing the pepper invalidates every stored secret. They are short-lived, so a rotation only makes outstanding
 * codes fail once; users request new ones.
 */
final class PepperedArgon2PasswordEncoder implements PasswordEncoder {

    /** Minimum pepper length: 32 characters carry at least the 256 bits HMAC-SHA256 can use when random. */
    static final int MIN_PEPPER_LENGTH = 32;

    private static final String HMAC = "HmacSHA256";

    private final Argon2PasswordEncoder argon2 = Argon2PasswordEncoder.defaultsForSpringSecurity_v5_8();

    private final SecretKeySpec pepper;

    /**
     * Creates the encoder.
     *
     * @param pepper the server-side secret, at least {@value #MIN_PEPPER_LENGTH} characters
     * @throws IllegalArgumentException if the pepper is shorter (the message never contains it)
     */
    PepperedArgon2PasswordEncoder(String pepper) {
        if (pepper == null || pepper.length() < MIN_PEPPER_LENGTH) {
            throw new IllegalArgumentException("FRAPPE_SECRET_PEPPER must have at least " + MIN_PEPPER_LENGTH
                    + " characters; generate one with 'openssl rand -base64 48'");
        }
        this.pepper = new SecretKeySpec(pepper.getBytes(StandardCharsets.UTF_8), HMAC);
    }

    @Override
    public @Nullable String encode(@Nullable CharSequence rawPassword) {
        return rawPassword == null ? null : argon2.encode(pepper(rawPassword));
    }

    @Override
    public boolean matches(@Nullable CharSequence rawPassword, @Nullable String encodedPassword) {
        return rawPassword != null && argon2.matches(pepper(rawPassword), encodedPassword);
    }

    private String pepper(CharSequence value) {
        try {
            // Mac instances are not thread-safe and cheap to create next to an Argon2 run.
            var mac = Mac.getInstance(HMAC);
            mac.init(pepper);
            return HexFormat.of().formatHex(mac.doFinal(value.toString().getBytes(StandardCharsets.UTF_8)));
        } catch (GeneralSecurityException e) {
            throw new IllegalStateException("HmacSHA256 is unavailable in this JVM", e);
        }
    }
}
