package com.frappe.platform;

import com.fasterxml.jackson.annotation.JsonIgnore;
import com.github.kagkarlsson.scheduler.task.helper.ScheduleAndData;
import com.github.kagkarlsson.scheduler.task.schedule.CronSchedule;
import com.github.kagkarlsson.scheduler.task.schedule.Schedule;
import java.time.ZoneId;
import java.util.Objects;

/**
 * The schedule of one entity's recurring task (a branch's business-day close, for example): a cron expression read in
 * the entity's time zone. Stored as the task data of that entity's execution, so changing it (a branch moves to another
 * zone) reschedules only that execution, without a restart. See {@link ScheduledTasks#perEntity}. Stored as JSON with
 * exactly these two fields.
 *
 * @param cron Spring-style cron expression with six fields, seconds first ({@code 0 0 4 * * *} is 04:00 daily)
 * @param zone the zone the expression is read in, so local times follow daylight saving
 */
public record EntitySchedule(String cron, ZoneId zone) implements ScheduleAndData {

    /**
     * Validates the schedule, so an invalid expression fails where it is set, not when the execution comes due.
     *
     * @param cron Spring-style cron expression with six fields
     * @param zone the zone the expression is read in
     * @throws IllegalArgumentException if the expression is not valid cron
     */
    public EntitySchedule {
        Objects.requireNonNull(cron, "cron");
        Objects.requireNonNull(zone, "zone");
        toCronSchedule(cron, zone);
    }

    /**
     * The same expression read in another zone.
     *
     * @param newZone the zone the expression is read in from now on
     * @return the moved schedule
     */
    public EntitySchedule withZone(ZoneId newZone) {
        return new EntitySchedule(cron, newZone);
    }

    /**
     * The schedule db-scheduler computes the next execution from.
     *
     * @return the cron schedule in {@link #zone()}
     */
    // Derived from cron and zone: storing it as well would keep a second copy that could disagree.
    @JsonIgnore
    @Override
    public Schedule getSchedule() {
        return toCronSchedule(cron, zone);
    }

    /**
     * No data beyond the schedule: the task instance id already names the entity.
     *
     * @return always {@code null}
     */
    @JsonIgnore
    @Override
    public Object getData() {
        return null;
    }

    private static CronSchedule toCronSchedule(String cron, ZoneId zone) {
        try {
            return new CronSchedule(cron, zone);
        } catch (IllegalArgumentException e) {
            throw new IllegalArgumentException("Invalid cron expression '" + cron + "': " + e.getMessage(), e);
        }
    }
}
