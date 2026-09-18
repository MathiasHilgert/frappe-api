package com.frappe.platform;

import static org.assertj.core.api.Assertions.assertThat;

import java.time.Clock;
import java.time.Instant;
import java.time.ZoneOffset;
import org.junit.jupiter.api.Test;

class Uuid7Test {

    private final Clock clock = Clock.fixed(Instant.parse("2026-09-18T12:00:00.123Z"), ZoneOffset.UTC);

    @Test
    void createsVersion7IdsCarryingTheClockTimestamp() {
        var id = Uuid7.next(clock);

        assertThat(id.version()).isEqualTo(7);
        assertThat(id.variant()).isEqualTo(2);
        assertThat(id.getMostSignificantBits() >>> 16).isEqualTo(clock.millis());
    }

    @Test
    void createsDistinctIdsForTheSameInstant() {
        assertThat(Uuid7.next(clock)).isNotEqualTo(Uuid7.next(clock));
    }
}
