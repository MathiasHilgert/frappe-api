package com.frappe.platform.infrastructure.ids;

import com.frappe.platform.IdGenerator;
import com.github.f4b6a3.uuid.factory.standard.TimeOrderedEpochFactory;
import java.time.Clock;
import java.util.UUID;

/**
 * UUIDv7 (RFC 9562) generator backed by uuid-creator. The "plus 1" variant increments the random part when the
 * millisecond repeats, so ids from one generator strictly increase even under a frozen clock; B-tree inserts stay
 * append-only and ids sort by creation.
 */
final class UuidV7IdGenerator implements IdGenerator {

    private final TimeOrderedEpochFactory factory;

    /**
     * Creates a generator reading time from the given clock.
     *
     * @param clock source of the millisecond timestamp
     */
    UuidV7IdGenerator(Clock clock) {
        this.factory = TimeOrderedEpochFactory.builder()
                .withClock(clock)
                .withIncrementPlus1()
                .build();
    }

    @Override
    public UUID newId() {
        return factory.create();
    }
}
