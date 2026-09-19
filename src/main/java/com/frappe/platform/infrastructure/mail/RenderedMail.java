package com.frappe.platform.infrastructure.mail;

import java.util.Locale;

/**
 * A mail ready to deliver, entirely in one language.
 *
 * @param subject the subject line
 * @param html the complete HTML document (layout and body)
 * @param locale the language it was rendered in
 */
record RenderedMail(String subject, String html, Locale locale) {

    /**
     * Describes the mail without its content, which may carry one-time codes.
     *
     * @return the language and the size of the HTML
     */
    @Override
    public String toString() {
        return "RenderedMail[locale=%s, html=%d chars]".formatted(locale.toLanguageTag(), html.length());
    }
}
