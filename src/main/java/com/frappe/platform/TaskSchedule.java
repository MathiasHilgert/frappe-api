package com.frappe.platform;

import java.time.Duration;
import java.time.LocalTime;
import java.time.ZoneId;
import java.util.Objects;

/**
 * When a recurring task runs. Local times are read in an explicit zone, never the server's, so they follow daylight
 * saving where the business is.
 */
public sealed interface TaskSchedule {

    /**
     * Runs again a fixed time after the previous run ended, so runs never overlap.
     *
     * @param delay the pause between the end of one run and the start of the next
     * @return the schedule
     * @throws IllegalArgumentException if the delay is not positive
     */
    static TaskSchedule fixedDelay(Duration delay) {
        return new FixedDelay(delay);
    }

    /**
     * Runs once a day at a local time.
     *
     * @param time the local time
     * @param zone the zone the time is read in
     * @return the schedule
     */
    static TaskSchedule daily(LocalTime time, ZoneId zone) {
        return new Daily(time, zone);
    }

    /**
     * Runs on a cron expression. The expression is checked when the task is declared, at startup.
     *
     * @param expression Spring-style cron with six fields, seconds first ({@code 0 0 4 * * *} is 04:00 daily)
     * @param zone the zone the expression is read in
     * @return the schedule
     */
    static TaskSchedule cron(String expression, ZoneId zone) {
        return new Cron(expression, zone);
    }

    /**
     * A fixed pause between runs.
     *
     * @param delay the pause between the end of one run and the start of the next; positive
     */
    record FixedDelay(Duration delay) implements TaskSchedule {

        /**
         * Validates the delay.
         *
         * @param delay the pause; must be positive
         */
        public FixedDelay {
            Objects.requireNonNull(delay, "delay");
            if (delay.isNegative() || delay.isZero()) {
                throw new IllegalArgumentException("A fixed delay must be positive, was " + delay);
            }
        }
    }

    /**
     * Once a day at a local time.
     *
     * @param time the local time
     * @param zone the zone the time is read in
     */
    record Daily(LocalTime time, ZoneId zone) implements TaskSchedule {

        /**
         * Validates the schedule.
         *
         * @param time the local time; required
         * @param zone the zone; required
         */
        public Daily {
            Objects.requireNonNull(time, "time");
            Objects.requireNonNull(zone, "zone");
        }
    }

    /**
     * A cron expression.
     *
     * @param expression Spring-style cron with six fields, seconds first
     * @param zone the zone the expression is read in
     */
    record Cron(String expression, ZoneId zone) implements TaskSchedule {

        /**
         * Validates the schedule.
         *
         * @param expression the cron expression; required
         * @param zone the zone; required
         */
        public Cron {
            Objects.requireNonNull(expression, "expression");
            Objects.requireNonNull(zone, "zone");
        }
    }
}
