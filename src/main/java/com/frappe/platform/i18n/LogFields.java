package com.frappe.platform.i18n;

/** Structured log field names of the localization package (see {@code writing-code/references/logging.md}). */
final class LogFields {

    /** The raw {@code Accept-Language} header a request carried. */
    static final String ACCEPT_LANGUAGE = "http.request.headers.accept_language";

    private LogFields() {}
}
