package com.frappe.platform.infrastructure.scheduling;

import com.fasterxml.jackson.annotation.JsonIgnore;
import com.frappe.platform.EntitySchedule;
import com.github.kagkarlsson.scheduler.task.helper.ScheduleAndData;
import com.github.kagkarlsson.scheduler.task.schedule.Schedule;
import java.time.ZoneId;

/**
 * An {@link EntitySchedule} as the task data of the entity's execution, stored as JSON with exactly these two fields;
 * db-scheduler computes the entity's next run from it.
 *
 * @param cron Spring-style cron with six fields, seconds first
 * @param zone the zone the expression is read in
 */
record StoredEntitySchedule(String cron, ZoneId zone) implements ScheduleAndData {

    /**
     * Validates the expression, so an invalid one fails where the schedule is set, not when the run comes due.
     *
     * @param cron the cron expression
     * @param zone the zone
     * @throws IllegalArgumentException if the expression is invalid
     */
    StoredEntitySchedule {
        LibrarySchedules.cron(cron, zone);
    }

    /**
     * The stored form of a platform schedule.
     *
     * @param schedule the platform schedule
     * @return the stored form
     * @throws IllegalArgumentException if its cron expression is invalid
     */
    static StoredEntitySchedule of(EntitySchedule schedule) {
        return new StoredEntitySchedule(schedule.cron(), schedule.zone());
    }

    /**
     * The platform schedule this was stored from.
     *
     * @return the schedule
     */
    EntitySchedule toEntitySchedule() {
        return new EntitySchedule(cron, zone);
    }

    /**
     * The schedule db-scheduler computes the next run from.
     *
     * @return the cron schedule in {@link #zone()}
     */
    // Derived from cron and zone: storing it as well would keep a second copy that could disagree.
    @JsonIgnore
    @Override
    public Schedule getSchedule() {
        return LibrarySchedules.cron(cron, zone);
    }

    /**
     * No data beyond the schedule: the instance id is the entity key.
     *
     * @return always {@code null}
     */
    @JsonIgnore
    @Override
    public Object getData() {
        return null;
    }
}
