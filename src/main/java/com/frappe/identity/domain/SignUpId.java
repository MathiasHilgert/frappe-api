package com.frappe.identity.domain;

import java.util.Objects;
import java.util.UUID;

/**
 * Identifies a sign-up.
 *
 * @param value the UUIDv7
 */
public record SignUpId(UUID value) {

    /**
     * Validates the value.
     *
     * @param value the UUIDv7, not {@code null}
     */
    public SignUpId {
        Objects.requireNonNull(value, "value");
    }
}
