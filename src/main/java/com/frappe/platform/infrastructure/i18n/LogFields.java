package com.frappe.platform.infrastructure.i18n;

/**
 * Names of the structured log fields (SLF4J key/values) this package emits. ECS output nests them by dot; stable for
 * log queries.
 */
final class LogFields {

    /** The link of the locale chain a log line is about ({@code user_preference}, {@code tenant_defaults}). */
    static final String LOCALE_LINK = "frappe.locale_link";

    /** The raw {@code Accept-Language} header a request carried. */
    static final String ACCEPT_LANGUAGE = "http.request.headers.accept_language";

    /** The target language (BCP 47 tag) of a machine translation call. */
    static final String TARGET_LANGUAGE = "frappe.target_language";

    /** What a machine translation call resulted in ({@code unavailable}, {@code quota_exceeded}). */
    static final String TRANSLATION_OUTCOME = "frappe.translation_outcome";

    private LogFields() {}
}
