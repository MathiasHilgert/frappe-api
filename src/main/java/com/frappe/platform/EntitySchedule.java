package com.frappe.platform;

import java.time.ZoneId;
import java.util.Objects;

/**
 * The schedule of one entity's recurring task (a branch's business-day close, for example): a cron expression read in
 * the entity's time zone. Changing it (a branch moves to another zone) moves only that entity's next run, without a
 * restart. See {@link EntityTask}.
 *
 * @param cron Spring-style cron with six fields, seconds first ({@code 0 0 4 * * *} is 04:00 daily); checked when the
 *     schedule is set with {@link EntityTask#schedule}
 * @param zone the zone the expression is read in, so local times follow daylight saving
 */
public record EntitySchedule(String cron, ZoneId zone) {

    /**
     * Requires both parts.
     *
     * @param cron the cron expression
     * @param zone the zone
     */
    public EntitySchedule {
        Objects.requireNonNull(cron, "cron");
        Objects.requireNonNull(zone, "zone");
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
}
