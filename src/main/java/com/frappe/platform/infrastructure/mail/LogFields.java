package com.frappe.platform.infrastructure.mail;

/**
 * Names of the structured log fields (SLF4J key/values) this package emits. Never the recipient, the subject or the
 * body: they carry personal data and one-time codes.
 */
final class LogFields {

    /** Mail template id ({@code identity/email-proof}). */
    static final String TEMPLATE = "frappe.mail.template";

    /** Language the mail was rendered in ({@code none} when rendering failed). */
    static final String LOCALE = "frappe.mail.locale";

    /** Mail provider ({@code resend}, {@code smtp}). */
    static final String PROVIDER = "frappe.mail.provider";

    private LogFields() {}
}
