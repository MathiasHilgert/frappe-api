package com.frappe.platform.infrastructure.ids;

import com.frappe.platform.IdGenerator;
import java.time.Clock;

/** Test access to the production UUIDv7 generator with a chosen clock. */
public final class TestIds {

    private TestIds() {}

    /**
     * Creates a UUIDv7 generator reading the given clock.
     *
     * @param clock the clock to read
     * @return the generator
     */
    public static IdGenerator withClock(Clock clock) {
        return new UuidV7IdGenerator(clock);
    }
}
