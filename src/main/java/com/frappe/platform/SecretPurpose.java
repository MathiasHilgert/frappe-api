package com.frappe.platform;

import java.time.Duration;

/**
 * What a short-lived secret is for and how it is issued, declared once per module as an enum whose constants carry
 * the lifetime and the issue cap, so call sites repeat neither strings nor numbers:
 *
 * <pre>{@code
 * public enum IdentitySecrets implements SecretPurpose {
 *     EMAIL_PROOF(Duration.ofMinutes(15), Duration.ofHours(1), 5),
 *     RECOVERY(Duration.ofMinutes(30), Duration.ofHours(24), 3);
 *     // constructor storing the three values, module() returning "identity"
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

    /**
     * How long an issued secret stays valid.
     *
     * @return the time to live; at least 1 ms
     */
    Duration ttl();

    /**
     * Length of the sliding window of the issue cap.
     *
     * @return the window; at least 1 ms
     */
    Duration issueWindow();

    /**
     * Secrets that may be issued to one subject within any {@link #issueWindow()}.
     *
     * @return the issue limit; positive
     */
    int issueLimit();
}
