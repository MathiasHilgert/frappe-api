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
     * The token bucket of a rate limit. The definition is part of the key, so a changed limit takes effect at once
     * with a fresh bucket, and the old bucket expires on its own.
     *
     * @param key the limit's key
     * @return {@code frappe:rate-limit:<module>:<purpose>:<subject>:<capacity>-per-<period in ms>ms}
     */
    static String rateLimit(LimitKey key) {
        return "frappe:rate-limit:" + key.module() + ":" + key.purpose() + ":" + key.subject() + ":" + key.capacity()
                + "-per-" + key.period().toMillis() + "ms";
    }

    private static String suffix(SecretKey key) {
        return key.module() + ":" + key.purpose() + ":" + key.subjectId();
    }
}
