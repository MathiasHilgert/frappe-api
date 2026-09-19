package com.frappe.platform.infrastructure.valkey;

import com.frappe.platform.LimitKey;
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

    /**
     * The token bucket of a rate limit.
     *
     * @param key the limit's key
     * @return {@code frappe:rate-limit:<module>:<purpose>:<subject>}
     */
    static String rateLimit(LimitKey key) {
        return "frappe:rate-limit:" + key.module() + ":" + key.purpose() + ":" + key.subject();
    }

    private static String suffix(SecretKey key) {
        return key.module() + ":" + key.purpose() + ":" + key.subjectId();
    }
}
