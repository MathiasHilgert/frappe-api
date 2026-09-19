package com.frappe.platform;

import java.util.Locale;
import java.util.Objects;
import java.util.UUID;
import java.util.regex.Pattern;

/**
 * Identifies one short-lived secret: the owning module, what the secret is for, and whom it was issued to. Build it
 * from a module's typed purpose with {@link #of(SecretPurpose, UUID)}. Keys are
 * visible in Valkey tooling, so they hold ids and fixed names only; lowercase kebab-case names cannot carry an email
 * address or other personal data.
 *
 * @param module owning module, e.g. {@code identity}
 * @param purpose what the secret proves, e.g. {@code email-verification}
 * @param subjectId id of the person or account the secret was issued to
 */
public record SecretKey(String module, String purpose, UUID subjectId) {

    private static final Pattern NAME = Pattern.compile("[a-z][a-z0-9]*(-[a-z0-9]+)*");

    private static final Pattern CONSTANT = Pattern.compile("[A-Z][A-Z0-9]*(_[A-Z0-9]+)*");

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
     * The key of a secret issued for a typed purpose.
     *
     * @param purpose the module's purpose, e.g. {@code IdentitySecrets.EMAIL_PROOF}
     * @param subjectId id of the person or account the secret is issued to
     * @return the key, e.g. {@code identity}, {@code email-proof}, the subject id
     * @throws IllegalArgumentException if the module is not kebab-case or the name not UPPER_SNAKE_CASE
     */
    public static SecretKey of(SecretPurpose purpose, UUID subjectId) {
        return new SecretKey(purpose.module(), keyName(purpose.name()), subjectId);
    }

    /**
     * Turns an UPPER_SNAKE_CASE purpose name into its kebab-case key name.
     *
     * @param constant the purpose name, e.g. {@code EMAIL_PROOF}
     * @return the key name, e.g. {@code email-proof}
     * @throws IllegalArgumentException if the name is not UPPER_SNAKE_CASE
     */
    static String keyName(String constant) {
        Objects.requireNonNull(constant, "name");
        if (!CONSTANT.matcher(constant).matches()) {
            throw new IllegalArgumentException("purpose name must be UPPER_SNAKE_CASE, was '" + constant + "'");
        }
        return constant.toLowerCase(Locale.ROOT).replace('_', '-');
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
