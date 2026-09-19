/**
 * Transactional email: renders a {@link com.frappe.platform.mail.MailMessage} in one language with JTE (templates and
 * the MJML layout precompiled at build time, text from the ICU catalogs) and delivers it through Resend, or SMTP to
 * Mailpit in the {@code local} profile.
 */
package com.frappe.platform.infrastructure.mail;
