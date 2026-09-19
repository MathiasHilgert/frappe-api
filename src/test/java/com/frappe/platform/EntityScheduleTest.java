package com.frappe.platform;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatNullPointerException;

import java.time.ZoneId;
import org.junit.jupiter.api.Test;

class EntityScheduleTest {

    static final String DAILY_AT_FOUR = "0 0 4 * * *";

    @Test
    void movesToAnotherZoneKeepingItsCron() {
        // Given
        var saoPaulo = new EntitySchedule(DAILY_AT_FOUR, ZoneId.of("America/Sao_Paulo"));

        // When
        var berlin = saoPaulo.withZone(ZoneId.of("Europe/Berlin"));

        // Then
        assertThat(berlin).isEqualTo(new EntitySchedule(DAILY_AT_FOUR, ZoneId.of("Europe/Berlin")));
    }

    @Test
    void requiresACronAndAZone() {
        assertThatNullPointerException().isThrownBy(() -> new EntitySchedule(null, ZoneId.of("UTC")));
        assertThatNullPointerException().isThrownBy(() -> new EntitySchedule(DAILY_AT_FOUR, null));
    }
}
