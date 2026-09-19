package com.frappe.platform;

import java.net.Inet4Address;
import java.net.Inet6Address;
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
 * <p>Keys are visible in Valkey tooling, so the subject is an id or a network address only, never an email address or
 * another personal value; build keys with {@link #ofId} or {@link #ofAddress}. The constructor accepts exactly the
 * canonical forms those factories produce.
 *
 * @param module owning module, lowercase kebab-case, e.g. {@code identity}
 * @param purpose what is limited, lowercase kebab-case, e.g. {@code login}
 * @param subject a canonical lowercase UUID, IPv4 address, or IPv6 /64 prefix ({@code 2001:db8:1:2::/64})
 * @param capacity calls allowed per period; positive
 * @param period time in which a full bucket refills; positive
 */
public record LimitKey(String module, String purpose, String subject, long capacity, Duration period) {

    /** Four IPv6 groups in their shortest lowercase form, then the /64 suffix. */
    private static final Pattern IPV6_PREFIX =
            Pattern.compile("(0|[1-9a-f][0-9a-f]{0,3})(:(0|[1-9a-f][0-9a-f]{0,3})){3}::/64");

    private static final int IPV6_PREFIX_GROUPS = 4;

    /**
     * Validates the key.
     *
     * @throws IllegalArgumentException if a name is not lowercase kebab-case, the subject is not a canonical UUID, IPv4
     *     address or IPv6 /64 prefix, or the capacity or period is not positive
     * @throws NullPointerException if a component is {@code null}
     */
    public LimitKey {
        SecretKey.requireName(module, "module");
        SecretKey.requireName(purpose, "purpose");
        Objects.requireNonNull(subject, "subject");
        if (!isUuid(subject)
                && !isIpv4Address(subject)
                && !IPV6_PREFIX.matcher(subject).matches()) {
            throw new IllegalArgumentException(
                    "subject must be a canonical lowercase UUID, IPv4 address or IPv6 /64 prefix; use ofId or ofAddress");
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
     * A limit per client network address: an IPv4 address as is, an IPv6 address by its /64 prefix. One IPv6 client
     * (a home or mobile network) usually owns a whole /64 and can pick any address in it, so limiting single IPv6
     * addresses would not limit it at all.
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
        var subject =
                switch (address) {
                    case Inet4Address v4 -> v4.getHostAddress();
                    case Inet6Address v6 -> slash64(v6);
                    default -> throw new IllegalArgumentException("Unsupported address type " + address.getClass());
                };
        return new LimitKey(module, purpose, subject, capacity, period);
    }

    private static String slash64(Inet6Address address) {
        var bytes = address.getAddress();
        var groups = new StringBuilder();
        for (var group = 0; group < IPV6_PREFIX_GROUPS; group++) {
            var value = ((bytes[2 * group] & 0xff) << 8) | (bytes[2 * group + 1] & 0xff);
            groups.append(group == 0 ? "" : ":").append(Integer.toHexString(value));
        }
        return groups.append("::/64").toString();
    }

    private static boolean isUuid(String subject) {
        try {
            return UUID.fromString(subject).toString().equals(subject);
        } catch (IllegalArgumentException notAUuid) {
            return false;
        }
    }

    private static boolean isIpv4Address(String subject) {
        try {
            // A literal parser (never DNS) that also takes shortened forms like "127.1"; only the canonical dotted quad
            // survives the round trip, so digit strings such as phone numbers are rejected.
            return Inet4Address.ofLiteral(subject).getHostAddress().equals(subject);
        } catch (IllegalArgumentException notAnAddress) {
            return false;
        }
    }
}
