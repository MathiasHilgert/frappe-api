package com.frappe.platform;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatIllegalArgumentException;

import com.github.kagkarlsson.scheduler.boot.autoconfigure.Jackson3Serializer;
import com.github.kagkarlsson.scheduler.task.ExecutionComplete;
import java.nio.charset.StandardCharsets;
import java.time.Instant;
import java.time.ZoneId;
import org.junit.jupiter.api.Test;

class EntityScheduleTest {

    static final Instant NOW = Instant.parse("2026-09-18T12:00:00Z");

    static final String DAILY_AT_FOUR = "0 0 4 * * *";

    @Test
    void theNextExecutionFollowsTheCronInTheEntityZone() {
        // Given
        var schedule = new EntitySchedule(DAILY_AT_FOUR, ZoneId.of("America/Sao_Paulo"));

        // When
        var next = schedule.getSchedule().getNextExecutionTime(ExecutionComplete.simulatedSuccess(NOW));

        // Then
        assertThat(next).isEqualTo(Instant.parse("2026-09-19T07:00:00Z"));
    }

    @Test
    void anotherZoneMovesTheNextExecution() {
        // Given
        var saoPaulo = new EntitySchedule(DAILY_AT_FOUR, ZoneId.of("America/Sao_Paulo"));

        // When
        var berlin = saoPaulo.withZone(ZoneId.of("Europe/Berlin"));

        // Then
        assertThat(berlin.getSchedule().getNextExecutionTime(ExecutionComplete.simulatedSuccess(NOW)))
                .isEqualTo(Instant.parse("2026-09-19T02:00:00Z"));
        assertThat(berlin.cron()).isEqualTo(DAILY_AT_FOUR);
    }

    @Test
    void rejectsAnInvalidCronWhenCreated() {
        assertThatIllegalArgumentException()
                .isThrownBy(() -> new EntitySchedule("every day at four", ZoneId.of("UTC")))
                .withMessageContaining("every day at four");
    }

    @Test
    void carriesNoFurtherData() {
        assertThat(new EntitySchedule(DAILY_AT_FOUR, ZoneId.of("UTC")).getData())
                .isNull();
    }

    @Test
    void survivesTheJsonTaskDataSerializer() {
        // Given
        var serializer = new Jackson3Serializer();
        var schedule = new EntitySchedule(DAILY_AT_FOUR, ZoneId.of("Europe/Berlin"));

        // When
        var restored = serializer.deserialize(EntitySchedule.class, serializer.serialize(schedule));

        // Then
        assertThat(restored).isEqualTo(schedule);
    }

    @Test
    void isStoredAsItsCronAndZoneOnly() {
        // Given
        var serializer = new Jackson3Serializer();

        // When
        var json = new String(
                serializer.serialize(new EntitySchedule(DAILY_AT_FOUR, ZoneId.of("Europe/Berlin"))),
                StandardCharsets.UTF_8);

        // Then
        assertThat(json).isEqualTo("{\"cron\":\"0 0 4 * * *\",\"zone\":\"Europe/Berlin\"}");
    }
}
