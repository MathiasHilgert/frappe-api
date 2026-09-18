package com.frappe.platform;

import java.security.SecureRandom;
import java.time.Clock;
import java.util.UUID;

/** Time-ordered UUIDv7 (RFC 9562) identifiers: 48-bit Unix milliseconds from the clock, then random bits. */
public final class Uuid7 {

    private static final SecureRandom RANDOM = new SecureRandom();

    private Uuid7() {}

    public static UUID next(Clock clock) {
        long msb = (clock.millis() << 16) | 0x7000L | (RANDOM.nextInt() & 0x0FFFL);
        long lsb = (RANDOM.nextLong() & 0x3FFFFFFFFFFFFFFFL) | 0x8000000000000000L;
        return new UUID(msb, lsb);
    }
}
