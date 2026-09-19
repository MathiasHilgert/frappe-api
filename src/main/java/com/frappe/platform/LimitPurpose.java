package com.frappe.platform;

import java.time.Duration;

/**
 * What is rate-limited and how much, declared once per module as an enum whose constants carry the limit, so call
 * sites repeat no numbers:
 *
 * <pre>{@code
 * public enum IdentityLimits implements LimitPurpose {
 *     LOGIN_PER_ACCOUNT(5, Duration.ofMinutes(1)),
 *     LOGIN_PER_ADDRESS(20, Duration.ofMinutes(1));
 *     // constructor storing capacity and period, module() returning "identity"
 * }
 * }</pre>
 *
 * <p>The key name is derived from {@link #name()}: {@code LOGIN_PER_ACCOUNT} becomes {@code login-per-account}.
 */
public interface LimitPurpose {

    /**
     * The owning module.
     *
     * @return the module name, lowercase kebab-case, e.g. {@code identity}
     */
    String module();

    /**
     * The purpose in UPPER_SNAKE_CASE; enums implement it with their constant name.
     *
     * @return the purpose name, e.g. {@code LOGIN_PER_ACCOUNT}
     */
    String name();

    /**
     * Calls allowed per period for one subject.
     *
     * @return the bucket capacity; positive
     */
    long capacity();

    /**
     * Time in which an empty bucket refills completely.
     *
     * @return the period; whole milliseconds, at least 1 ms
     */
    Duration period();
}
