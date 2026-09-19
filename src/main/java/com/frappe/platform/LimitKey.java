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
 * Callers own the definitions; a changed definition takes effect at once, with a fresh bucket.
 *
 * <p>Keys are visible in Valkey tooling, so the subject is an id or a network address only, never an email address or
 * another personal value. Build keys from a module's typed {@link LimitPurpose}, which carries the definition, with
 * {@link #ofId(LimitPurpose, UUID)} or {@link #ofAddress(LimitPurpose, InetAddress)}; the constructor accepts exactly
 * the canonical forms those factories produce.
 *
 * @param module owning module, lowercase kebab-case, e.g. {@code identity}
 * @param purpose what is limited, lowercase kebab-case, e.g. {@code login}
 * @param subject a canonical lowercase UUID, IPv4 address, or IPv6 /64 prefix ({@code 2001:db8:1:2::/64})
 * @param capacity calls allowed per period; positive
 * @param period time in which a full bucket refills; whole milliseconds, at least 1 ms
 */
public record LimitKey(String module, String purpose, String subject, long capacity, Duration period) {

    /** Four IPv6 groups in their shortest lowercase form, then the /64 suffix. */
    private static final Pattern IPV6_PREFIX =
            Pattern.compile("(0|[1-9a-f][0-9a-f]{0,3})(:(0|[1-9a-f][0-9a-f]{0,3})){3}::/64");

    private static final int IPV6_PREFIX_GROUPS = 4;

    private static final int NANOS_PER_MILLI = 1_000_000;

    /**
     * Validates the key.
     *
     * @throws IllegalArgumentException if a name is not lowercase kebab-case, the subject is not a canonical UUID, IPv4
     *     address or IPv6 /64 prefix, the capacity is not positive, or the period is not whole milliseconds of at least
     *     1 ms
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
        // The Valkey key holds the period in milliseconds: a sub-millisecond part would let two definitions share a
        // bucket.
        if (period.toMillis() < 1 || period.getNano() % NANOS_PER_MILLI != 0) {
            throw new IllegalArgumentException("period must be whole milliseconds, at least 1 ms, was " + period);
        }
    }

    /**
     * A limit per id, e.g. per account, with the purpose's definition.
     *
     * @param purpose the module's limit, e.g. {@code IdentityLimits.LOGIN_PER_ACCOUNT}
     * @param id the subject's id
     * @return the key
     * @throws IllegalArgumentException if the purpose's names or definition are invalid
     */
    public static LimitKey ofId(LimitPurpose purpose, UUID id) {
        return of(purpose, id.toString());
    }

    /**
     * A limit per client network address, with the purpose's definition: an IPv4 address as is, an IPv6 address by
     * its /64 prefix. One IPv6 client (a home or mobile network) usually owns a whole /64 and can pick any address in
     * it, so limiting single IPv6 addresses would not limit it at all.
     *
     * @param purpose the module's limit, e.g. {@code IdentityLimits.LOGIN_PER_ADDRESS}
     * @param address the client address
     * @return the key
     * @throws IllegalArgumentException if the purpose's names or definition are invalid
     */
    public static LimitKey ofAddress(LimitPurpose purpose, InetAddress address) {
        var subject =
                switch (address) {
                    case Inet4Address v4 -> v4.getHostAddress();
                    case Inet6Address v6 -> slash64(v6);
                    default -> throw new IllegalArgumentException("Unsupported address type " + address.getClass());
                };
        return of(purpose, subject);
    }

    private static LimitKey of(LimitPurpose purpose, String subject) {
        return new LimitKey(
                purpose.module(), SecretKey.keyName(purpose.name()), subject, purpose.capacity(), purpose.period());
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
