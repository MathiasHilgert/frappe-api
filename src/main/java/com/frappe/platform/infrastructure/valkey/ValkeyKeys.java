package com.frappe.platform.infrastructure.valkey;

import com.frappe.platform.SecretKey;

/** Valkey key names. Every key starts with {@code frappe:} and holds only fixed names and ids. */
final class ValkeyKeys {

    private ValkeyKeys() {}

    /**
     * The hash holding a secret's Argon2 hash and failure count.
     *
     * @param key the secret's key
     * @return {@code frappe:secret:<module>:<purpose>:<subjectId>}
     */
    static String secret(SecretKey key) {
        return "frappe:secret:" + suffix(key);
    }

    /**
     * The sorted set logging recent issues of a secret, for its sliding-window cap.
     *
     * @param key the secret's key
     * @return {@code frappe:secret-issues:<module>:<purpose>:<subjectId>}
     */
    static String secretIssues(SecretKey key) {
        return "frappe:secret-issues:" + suffix(key);
    }

    private static String suffix(SecretKey key) {
        return key.module() + ":" + key.purpose() + ":" + key.subjectId();
    }
}
