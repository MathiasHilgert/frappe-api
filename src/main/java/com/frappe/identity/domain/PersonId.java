package com.frappe.identity.domain;

import java.util.Objects;
import java.util.UUID;

/**
 * Identifies a person.
 *
 * @param value the UUIDv7
 */
public record PersonId(UUID value) {

    /**
     * Validates the value.
     *
     * @param value the UUIDv7, not {@code null}
     */
    public PersonId {
        Objects.requireNonNull(value, "value");
    }
}
