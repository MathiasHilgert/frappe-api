package com.frappe.platform.infrastructure.scheduling;

import static org.assertj.core.api.Assertions.assertThat;

import java.time.Clock;
import java.time.Instant;
import java.time.ZoneOffset;
import org.junit.jupiter.api.Test;

class SchedulingConfigurationTest {

    @Test
    void theSchedulerReadsTimeFromTheApplicationClock() {
        // Given
        var clock = Clock.fixed(Instant.parse("2026-09-18T12:00:00Z"), ZoneOffset.UTC);

        // When
        var schedulerClock = new SchedulingConfiguration().dbSchedulerClock(clock);

        // Then
        assertThat(schedulerClock.now()).isEqualTo(Instant.parse("2026-09-18T12:00:00Z"));
    }
}
