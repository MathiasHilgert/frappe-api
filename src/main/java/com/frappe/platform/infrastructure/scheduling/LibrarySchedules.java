package com.frappe.platform.infrastructure.scheduling;

import com.frappe.platform.TaskSchedule;
import com.github.kagkarlsson.scheduler.task.schedule.CronSchedule;
import com.github.kagkarlsson.scheduler.task.schedule.Daily;
import com.github.kagkarlsson.scheduler.task.schedule.FixedDelay;
import com.github.kagkarlsson.scheduler.task.schedule.Schedule;
import java.time.ZoneId;

/** Translates the platform's {@link TaskSchedule} into db-scheduler's {@link Schedule}. */
final class LibrarySchedules {

    private LibrarySchedules() {}

    /**
     * The library schedule of a platform schedule.
     *
     * @param schedule the platform schedule
     * @return the equivalent library schedule
     * @throws IllegalArgumentException if a cron expression is invalid
     */
    static Schedule of(TaskSchedule schedule) {
        return switch (schedule) {
            case TaskSchedule.FixedDelay fixedDelay -> FixedDelay.of(fixedDelay.delay());
            case TaskSchedule.Daily daily -> new Daily(daily.zone(), daily.time());
            case TaskSchedule.Cron cron -> cron(cron.expression(), cron.zone());
        };
    }

    /**
     * A cron schedule, rejecting an invalid expression with a message that names it.
     *
     * @param expression Spring-style cron with six fields
     * @param zone the zone the expression is read in
     * @return the schedule
     * @throws IllegalArgumentException if the expression is invalid
     */
    static CronSchedule cron(String expression, ZoneId zone) {
        try {
            return new CronSchedule(expression, zone);
        } catch (IllegalArgumentException e) {
            throw new IllegalArgumentException("Invalid cron expression '" + expression + "': " + e.getMessage(), e);
        }
    }
}
