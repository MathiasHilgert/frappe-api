package com.frappe.platform;

import java.util.regex.Pattern;

/**
 * The name of a scheduled task, {@code <module>.<kebab-case-name>} ({@code platform.outbox-recovery}). It keys the
 * stored executions and tags the task's telemetry, so a deployed task is never renamed.
 *
 * @param value the name
 */
public record TaskName(String value) {

    private static final Pattern RULE = Pattern.compile("[a-z][a-z0-9]*\\.[a-z0-9]+(-[a-z0-9]+)*");

    /**
     * Validates the name.
     *
     * @param value the name
     * @throws IllegalArgumentException if it is not {@code <module>.<kebab-case-name>}
     */
    public TaskName {
        if (value == null || !RULE.matcher(value).matches()) {
            throw new IllegalArgumentException("Scheduled task name '" + value
                    + "' must be <module>.<kebab-case-name>, e.g. platform.outbox-recovery");
        }
    }

    /**
     * The name of a task.
     *
     * @param value {@code <module>.<kebab-case-name>}
     * @return the name
     * @throws IllegalArgumentException if it breaks the rule
     */
    public static TaskName of(String value) {
        return new TaskName(value);
    }

    /**
     * The name as written.
     *
     * @return the value
     */
    @Override
    public String toString() {
        return value;
    }
}
