package com.frappe.identity.domain;

import java.util.Objects;

/**
 * A stored password hash in the hasher's encoded form (algorithm, parameters, salt and digest). It never prints its
 * value.
 *
 * @param value the encoded hash
 */
public record PasswordHash(String value) {

    /**
     * Validates the value.
     *
     * @param value the encoded hash, not {@code null}
     */
    public PasswordHash {
        Objects.requireNonNull(value, "value");
    }

    @Override
    public String toString() {
        return "PasswordHash[<redacted>]";
    }
}
