package com.frappe.platform;

import java.net.InetAddress;
import java.time.Duration;
import java.util.Objects;
import java.util.UUID;
import java.util.regex.Pattern;

/**
 * Identifies one rate limit and defines it: a bucket of {@code capacity} calls per {@code period} for one subject, e.g.
 * 5 login attempts per minute per account, or 20 per minute per IP address. Tokens refill gradually over the period.
 * Callers own the definitions; a key keeps the definition it was first used with until its bucket is full again and
 * expires.
 *
 * <p>Keys are visible in Valkey tooling, so the subject is an id or an IP address only, never an email address; build
 * keys with {@link #ofId} or {@link #ofAddress}.
 *
 * @param module owning module, lowercase kebab-case, e.g. {@code identity}
 * @param purpose what is limited, lowercase kebab-case, e.g. {@code login}
 * @param subject a lowercase UUID or IP address
 * @param capacity calls allowed per period; positive
 * @param period time in which a full bucket refills; positive
 */
public record LimitKey(String module, String purpose, String subject, long capacity, Duration period) {

    /** Lowercase UUIDs and IPv4/IPv6 addresses: hexadecimal digits, dots, colons and dashes only. */
    private static final Pattern SUBJECT = Pattern.compile("[0-9a-f.:-]{1,45}");

    /**
     * Validates the key.
     *
     * @throws IllegalArgumentException if a name is not lowercase kebab-case, the subject is not an id or an IP address,
     *     or the capacity or period is not positive
     * @throws NullPointerException if a component is {@code null}
     */
    public LimitKey {
        SecretKey.requireName(module, "module");
        SecretKey.requireName(purpose, "purpose");
        Objects.requireNonNull(subject, "subject");
        if (!SUBJECT.matcher(subject).matches()) {
            throw new IllegalArgumentException("subject must be a lowercase UUID or an IP address");
        }
        if (capacity < 1) {
            throw new IllegalArgumentException("capacity must be positive, was " + capacity);
        }
        Objects.requireNonNull(period, "period");
        if (period.isNegative() || period.isZero()) {
            throw new IllegalArgumentException("period must be positive, was " + period);
        }
    }

    /**
     * A limit per id, e.g. per account.
     *
     * @param module owning module
     * @param purpose what is limited
     * @param id the subject's id
     * @param capacity calls allowed per period
     * @param period time in which a full bucket refills
     * @return the key
     */
    public static LimitKey ofId(String module, String purpose, UUID id, long capacity, Duration period) {
        return new LimitKey(module, purpose, id.toString(), capacity, period);
    }

    /**
     * A limit per client IP address, in its canonical textual form without an IPv6 scope.
     *
     * @param module owning module
     * @param purpose what is limited
     * @param address the client address
     * @param capacity calls allowed per period
     * @param period time in which a full bucket refills
     * @return the key
     */
    public static LimitKey ofAddress(
            String module, String purpose, InetAddress address, long capacity, Duration period) {
        var text = address.getHostAddress();
        var scope = text.indexOf('%');
        return new LimitKey(module, purpose, scope < 0 ? text : text.substring(0, scope), capacity, period);
    }
}
