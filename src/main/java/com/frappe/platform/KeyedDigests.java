package com.frappe.platform;

import java.util.UUID;

/**
 * Turns a personal value, e.g. an email address, a recovery code or a PIN, into a stable id or digest keyed with a
 * server-side secret that never leaves the platform. Modules use the id where only ids may appear (a
 * {@link LimitKey} or {@link SecretKey} subject) and the digest to look a value up without storing it.
 *
 * <p>Results are deterministic: the same namespace and value give the same result on every instance sharing the key.
 * A value gets unrelated results in two namespaces. Values are used exactly as given; callers normalize them first,
 * e.g. lowercase a trimmed email address.
 *
 * <p>A namespace is {@code <module>.<kebab-name>}, both parts lowercase kebab-case, e.g. {@code identity.email}.
 */
public interface KeyedDigests {

    /**
     * A stable id for a personal value.
     *
     * @param namespace {@code <module>.<kebab-name>}, e.g. {@code identity.email}
     * @param value the normalized value
     * @return an RFC 9562 version-8 UUID derived from the keyed digest
     * @throws IllegalArgumentException if the namespace is not {@code <module>.<kebab-name>}
     * @throws NullPointerException if an argument is {@code null}
     */
    UUID subjectOf(String namespace, String value);

    /**
     * A keyed digest of a personal value, to store and look up instead of the value.
     *
     * @param namespace {@code <module>.<kebab-name>}, e.g. {@code identity.recovery-code}
     * @param value the normalized value
     * @return the base64url HMAC-SHA256 without padding, 43 characters
     * @throws IllegalArgumentException if the namespace is not {@code <module>.<kebab-name>}
     * @throws NullPointerException if an argument is {@code null}
     */
    String digestOf(String namespace, String value);
}
