package com.frappe.platform.infrastructure.digests;

import com.frappe.platform.KeyedDigests;
import java.nio.ByteBuffer;
import java.nio.charset.StandardCharsets;
import java.security.GeneralSecurityException;
import java.util.Base64;
import java.util.Objects;
import java.util.UUID;
import java.util.regex.Pattern;
import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;

/**
 * {@link KeyedDigests} with the JDK's HMAC-SHA256 over the namespace, a {@code 0x00} separator and the UTF-8 value.
 * Namespaces cannot contain {@code 0x00}, so one value never collides across namespaces. The key stays in this class:
 * with it, a PIN or code digest falls to a trivial offline search.
 */
final class HmacKeyedDigests implements KeyedDigests {

    /** Minimum key length: 32 characters carry at least the 256 bits HMAC-SHA256 can use when random. */
    static final int MIN_KEY_LENGTH = 32;

    private static final String HMAC = "HmacSHA256";

    private static final String KEBAB = "[a-z][a-z0-9]*(-[a-z0-9]+)*";

    private static final Pattern NAMESPACE = Pattern.compile(KEBAB + "\\." + KEBAB);

    private static final byte SEPARATOR = 0x00;

    private static final Base64.Encoder BASE64URL = Base64.getUrlEncoder().withoutPadding();

    private final SecretKeySpec key;

    /**
     * Creates the digests.
     *
     * @param key the digest key ({@code FRAPPE_DIGEST_PEPPER}), at least {@value #MIN_KEY_LENGTH} characters
     * @throws IllegalArgumentException if the key is missing or shorter (the message never contains it)
     */
    HmacKeyedDigests(String key) {
        if (key == null || key.length() < MIN_KEY_LENGTH) {
            throw new IllegalArgumentException("FRAPPE_DIGEST_PEPPER must have at least " + MIN_KEY_LENGTH
                    + " characters; generate one with 'openssl rand -base64 48'");
        }
        this.key = new SecretKeySpec(key.getBytes(StandardCharsets.UTF_8), HMAC);
    }

    @Override
    public UUID subjectOf(String namespace, String value) {
        var bytes = ByteBuffer.wrap(mac(namespace, value));
        var most = bytes.getLong();
        var least = bytes.getLong();
        // RFC 9562 version 8 (custom): version nibble 0b1000, variant bits 0b10.
        most = (most & ~0xF000L) | 0x8000L;
        least = (least & 0x3FFF_FFFF_FFFF_FFFFL) | Long.MIN_VALUE;
        return new UUID(most, least);
    }

    @Override
    public String digestOf(String namespace, String value) {
        return BASE64URL.encodeToString(mac(namespace, value));
    }

    private byte[] mac(String namespace, String value) {
        Objects.requireNonNull(namespace, "namespace");
        Objects.requireNonNull(value, "value");
        if (!NAMESPACE.matcher(namespace).matches()) {
            throw new IllegalArgumentException(
                    "namespace must be <module>.<kebab-name> in lowercase kebab-case (e.g. identity.email), was '"
                            + namespace + "'");
        }
        try {
            // Mac instances are not thread-safe and cheap to create.
            var mac = Mac.getInstance(HMAC);
            mac.init(key);
            mac.update(namespace.getBytes(StandardCharsets.UTF_8));
            mac.update(SEPARATOR);
            return mac.doFinal(value.getBytes(StandardCharsets.UTF_8));
        } catch (GeneralSecurityException e) {
            throw new IllegalStateException("HmacSHA256 is unavailable in this JVM", e);
        }
    }
}
