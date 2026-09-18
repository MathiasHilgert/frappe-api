package com.frappe.platform.infrastructure.ids;

import static org.assertj.core.api.Assertions.assertThat;

import java.time.Clock;
import java.time.Instant;
import java.time.ZoneOffset;
import java.util.UUID;
import java.util.stream.Stream;
import org.junit.jupiter.api.Test;

class UuidV7IdGeneratorTest {

    private static final int ID_COUNT = 1_000;

    @Test
    void generatesVersion7IdsThatStrictlyIncreaseWithinTheSameMillisecond() {
        // Given a clock frozen on one millisecond
        var clock = Clock.fixed(Instant.parse("2026-09-18T12:00:00.123Z"), ZoneOffset.UTC);
        var generator = new UuidV7IdGenerator(clock);

        // When many ids are generated
        var ids = Stream.generate(generator::newId).limit(ID_COUNT).toList();

        // Then every id is a v7 carrying that millisecond, in strictly increasing order
        assertThat(ids).allSatisfy(id -> {
            assertThat(id.version()).isEqualTo(7);
            assertThat(id.getMostSignificantBits() >>> 16).isEqualTo(clock.millis());
        });
        assertThat(ids).isSortedAccordingTo(UUID::compareTo).doesNotHaveDuplicates();
    }
}
