package com.frappe.platform.infrastructure.mail;

import org.springframework.boot.context.properties.ConfigurationProperties;
import org.springframework.boot.context.properties.bind.DefaultValue;

/**
 * Mail delivery settings ({@code frappe.mail.*}). Outside the {@code local} profile {@link RequiredMailSettings} fails
 * startup unless the sender and, for Resend, the API key are set.
 *
 * @param provider who delivers: {@code resend} (default) or {@code smtp} (Spring Mail, {@code spring.mail.*}; Mailpit
 *     in the {@code local} profile)
 * @param from the sender, for example {@code Frappé <no-reply@example.com>} (env {@code FRAPPE_MAIL_FROM})
 * @param resend Resend's settings
 */
@ConfigurationProperties("frappe.mail")
record MailDeliveryProperties(
        @DefaultValue("resend") Provider provider,
        String from,
        @DefaultValue Resend resend) {

    /** Who delivers mail. */
    enum Provider {
        /** Resend's API, outside the {@code local} profile. */
        RESEND,
        /** SMTP through Spring Mail, Mailpit in the {@code local} profile. */
        SMTP
    }

    /**
     * Resend's settings.
     *
     * @param apiKey the API key (env {@code RESEND_API_KEY}); never logged
     */
    record Resend(String apiKey) {

        /**
         * Describes the settings without the key.
         *
         * @return a redacted description
         */
        @Override
        public String toString() {
            return "Resend[apiKey=<redacted>]";
        }
    }
}
