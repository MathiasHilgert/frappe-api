package com.frappe.platform.infrastructure.i18n;

/**
 * Names of the structured log fields (SLF4J key/values) this package emits. ECS output nests them by dot; stable for
 * log queries.
 */
final class LogFields {

    /** The link of the locale chain a log line is about ({@code user_preference}, {@code tenant_defaults}). */
    static final String LOCALE_LINK = "frappe.locale_link";

    private LogFields() {}
}
