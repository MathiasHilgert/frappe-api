package com.frappe.platform.infrastructure.mail;

import com.frappe.platform.mail.MailDeliveryException;
import com.frappe.platform.mail.MailMessage;

/** Hands a rendered mail to a provider: Resend outside {@code local}, SMTP (Mailpit) in {@code local}. */
interface MailTransport {

    /**
     * Names the provider for telemetry.
     *
     * @return a short, stable name such as {@code resend} or {@code smtp}
     */
    String provider();

    /**
     * Delivers a rendered mail to the message's recipient.
     *
     * @param mail the rendered subject and HTML
     * @param message the message it was rendered from (recipient, idempotency key)
     * @throws MailDeliveryException if the provider refused the mail or could not be reached; the message never carries
     *     the recipient, the body or credentials
     */
    void deliver(RenderedMail mail, MailMessage message);
}
