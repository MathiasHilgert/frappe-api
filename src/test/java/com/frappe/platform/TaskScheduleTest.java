package com.frappe.platform;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatIllegalArgumentException;
import static org.assertj.core.api.Assertions.assertThatNullPointerException;

import java.time.Duration;
import java.time.LocalTime;
import java.time.ZoneId;
import org.junit.jupiter.api.Test;

class TaskScheduleTest {

    static final ZoneId BERLIN = ZoneId.of("Europe/Berlin");

    @Test
    void offersAFixedDelayADailyTimeAndACronExpression() {
        assertThat(TaskSchedule.fixedDelay(Duration.ofMinutes(1)))
                .isEqualTo(new TaskSchedule.FixedDelay(Duration.ofMinutes(1)));
        assertThat(TaskSchedule.daily(LocalTime.of(4, 0), BERLIN))
                .isEqualTo(new TaskSchedule.Daily(LocalTime.of(4, 0), BERLIN));
        assertThat(TaskSchedule.cron("0 0 4 * * *", BERLIN)).isEqualTo(new TaskSchedule.Cron("0 0 4 * * *", BERLIN));
    }

    @Test
    void rejectsANonPositiveDelay() {
        assertThatIllegalArgumentException()
                .isThrownBy(() -> TaskSchedule.fixedDelay(Duration.ZERO))
                .withMessageContaining("PT0S");
    }

    @Test
    void requiresAZone() {
        assertThatNullPointerException().isThrownBy(() -> TaskSchedule.daily(LocalTime.NOON, null));
        assertThatNullPointerException().isThrownBy(() -> TaskSchedule.cron("0 0 4 * * *", null));
    }
}
