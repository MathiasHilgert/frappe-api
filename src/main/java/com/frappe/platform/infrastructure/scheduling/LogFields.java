package com.frappe.platform.infrastructure.scheduling;

/**
 * Names of the structured log fields (SLF4J key/values) this package emits, namespaced under {@code
 * frappe.scheduling}. ECS output nests them by dot; stable for log queries.
 */
final class LogFields {

    /** Task name, {@code <module>.<kebab-case-name>}. */
    static final String TASK_NAME = "frappe.scheduling.task_name";

    /** Task instance id, the natural key of the execution. */
    static final String TASK_INSTANCE = "frappe.scheduling.task_instance";

    /** Failures in a row of this execution, the one being logged included. */
    static final String CONSECUTIVE_FAILURES = "frappe.scheduling.consecutive_failures";

    private LogFields() {}
}
