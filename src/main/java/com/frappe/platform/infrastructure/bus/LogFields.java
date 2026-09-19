package com.frappe.platform.infrastructure.bus;

/**
 * Names of the structured log fields (SLF4J key/values) this package emits: lowercase, dotted, namespaced under
 * {@code frappe.use_case}. ECS output nests them by dot; stable for log queries.
 */
final class LogFields {

    /** Kind of use case: {@code command} or {@code query}. */
    static final String USE_CASE_KIND = "frappe.use_case.kind";

    /** Number of handlers registered for one kind. */
    static final String HANDLER_COUNT = "frappe.use_case.handlers";

    private LogFields() {}
}
