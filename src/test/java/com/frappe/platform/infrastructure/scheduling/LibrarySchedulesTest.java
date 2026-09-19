package com.frappe.platform.infrastructure.scheduling;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatIllegalArgumentException;

import com.frappe.platform.TaskSchedule;
import com.github.kagkarlsson.scheduler.task.ExecutionComplete;
import com.github.kagkarlsson.scheduler.task.schedule.FixedDelay;
import java.time.Duration;
import java.time.Instant;
import java.time.LocalTime;
import java.time.ZoneId;
import org.junit.jupiter.api.Test;

class LibrarySchedulesTest {

    static final Instant NOW = Instant.parse("2026-09-18T12:00:00Z");

    static final ZoneId SAO_PAULO = ZoneId.of("America/Sao_Paulo");

    @Test
    void aFixedDelayIsTheLibrarysFixedDelay() {
        assertThat(LibrarySchedules.of(TaskSchedule.fixedDelay(Duration.ofMinutes(1))))
                .isEqualTo(FixedDelay.of(Duration.ofMinutes(1)));
    }

    @Test
    void aDailyTimeRunsNextAtThatLocalTimeInItsZone() {
        var schedule = LibrarySchedules.of(TaskSchedule.daily(LocalTime.of(4, 0), SAO_PAULO));

        assertThat(schedule.getNextExecutionTime(ExecutionComplete.simulatedSuccess(NOW)))
                .isEqualTo(Instant.parse("2026-09-19T07:00:00Z"));
    }

    @Test
    void aCronRunsNextAtItsTimeInItsZone() {
        var schedule = LibrarySchedules.of(TaskSchedule.cron("0 30 4 * * *", SAO_PAULO));

        assertThat(schedule.getNextExecutionTime(ExecutionComplete.simulatedSuccess(NOW)))
                .isEqualTo(Instant.parse("2026-09-19T07:30:00Z"));
    }

    @Test
    void rejectsAnInvalidCron() {
        assertThatIllegalArgumentException()
                .isThrownBy(() -> LibrarySchedules.of(TaskSchedule.cron("every day at four", SAO_PAULO)))
                .withMessageContaining("every day at four");
    }
}
