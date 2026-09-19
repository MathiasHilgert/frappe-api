package com.frappe.platform;

/**
 * What a short-lived secret is for, declared once per module as an enum, so call sites name a purpose instead of
 * repeating strings:
 *
 * <pre>{@code
 * public enum IdentitySecrets implements SecretPurpose {
 *     EMAIL_PROOF, RECOVERY;
 *
 *     public String module() { return "identity"; }
 * }
 * }</pre>
 *
 * <p>The key name is derived from {@link #name()}: {@code EMAIL_PROOF} becomes {@code email-proof}.
 */
public interface SecretPurpose {

    /**
     * The owning module.
     *
     * @return the module name, lowercase kebab-case, e.g. {@code identity}
     */
    String module();

    /**
     * The purpose in UPPER_SNAKE_CASE; enums implement it with their constant name.
     *
     * @return the purpose name, e.g. {@code EMAIL_PROOF}
     */
    String name();
}
