package com.frappe.platform.infrastructure.scheduling;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatIllegalArgumentException;

import com.frappe.platform.EntitySchedule;
import com.github.kagkarlsson.scheduler.boot.autoconfigure.Jackson3Serializer;
import com.github.kagkarlsson.scheduler.task.ExecutionComplete;
import java.nio.charset.StandardCharsets;
import java.time.Instant;
import java.time.ZoneId;
import org.junit.jupiter.api.Test;

class StoredEntityScheduleTest {

    static final Instant NOW = Instant.parse("2026-09-18T12:00:00Z");

    static final String DAILY_AT_FOUR = "0 0 4 * * *";

    final Jackson3Serializer serializer = new Jackson3Serializer();

    @Test
    void theNextExecutionFollowsTheCronInTheEntityZone() {
        // Given
        var stored = StoredEntitySchedule.of(new EntitySchedule(DAILY_AT_FOUR, ZoneId.of("America/Sao_Paulo")));

        // When
        var next = stored.getSchedule().getNextExecutionTime(ExecutionComplete.simulatedSuccess(NOW));

        // Then
        assertThat(next).isEqualTo(Instant.parse("2026-09-19T07:00:00Z"));
    }

    @Test
    void rejectsAnInvalidCronWhenScheduled() {
        assertThatIllegalArgumentException()
                .isThrownBy(() -> StoredEntitySchedule.of(new EntitySchedule("every day at four", ZoneId.of("UTC"))))
                .withMessageContaining("every day at four");
    }

    @Test
    void isStoredAsItsCronAndZoneOnly() {
        // When
        var json = new String(
                serializer.serialize(
                        StoredEntitySchedule.of(new EntitySchedule(DAILY_AT_FOUR, ZoneId.of("Europe/Berlin")))),
                StandardCharsets.UTF_8);

        // Then
        assertThat(json).isEqualTo("{\"cron\":\"0 0 4 * * *\",\"zone\":\"Europe/Berlin\"}");
    }

    @Test
    void survivesTheJsonTaskDataSerializer() {
        // Given
        var stored = StoredEntitySchedule.of(new EntitySchedule(DAILY_AT_FOUR, ZoneId.of("Europe/Berlin")));

        // When
        var restored = serializer.deserialize(StoredEntitySchedule.class, serializer.serialize(stored));

        // Then
        assertThat(restored).isEqualTo(stored);
        assertThat(restored.toEntitySchedule())
                .isEqualTo(new EntitySchedule(DAILY_AT_FOUR, ZoneId.of("Europe/Berlin")));
    }
}
