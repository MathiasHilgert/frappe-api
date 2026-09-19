package com.frappe.platform;

import java.util.Objects;
import java.util.UUID;
import java.util.regex.Pattern;

/**
 * Identifies one short-lived secret: the owning module, what the secret is for, and whom it was issued to. Keys are
 * visible in Valkey tooling, so they hold ids and fixed names only; lowercase kebab-case names cannot carry an email
 * address or other personal data.
 *
 * @param module owning module, e.g. {@code identity}
 * @param purpose what the secret proves, e.g. {@code email-verification}
 * @param subjectId id of the person or account the secret was issued to
 */
public record SecretKey(String module, String purpose, UUID subjectId) {

    private static final Pattern NAME = Pattern.compile("[a-z][a-z0-9]*(-[a-z0-9]+)*");

    /**
     * Validates the key.
     *
     * @throws IllegalArgumentException if {@code module} or {@code purpose} is not lowercase kebab-case
     * @throws NullPointerException if any component is {@code null}
     */
    public SecretKey {
        requireName(module, "module");
        requireName(purpose, "purpose");
        Objects.requireNonNull(subjectId, "subjectId");
    }

    /**
     * Checks that a key name is lowercase kebab-case.
     *
     * @param name the name to check
     * @param component the component name, for the error message
     * @throws IllegalArgumentException if the name is not lowercase kebab-case
     * @throws NullPointerException if the name is {@code null}
     */
    static void requireName(String name, String component) {
        Objects.requireNonNull(name, component);
        if (!NAME.matcher(name).matches()) {
            throw new IllegalArgumentException(
                    component + " must be lowercase kebab-case (e.g. email-verification), was '" + name + "'");
        }
    }
}
