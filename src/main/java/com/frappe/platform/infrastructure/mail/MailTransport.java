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
     * @throws MailDeliveryException if delivery failed for a reason a retry can fix (outage, limit, settings); the
     *     outbox retries it. The message never carries the recipient, the body or credentials
     * @throws MailRejectedException if the provider refused this mail for good (a refused recipient); not retried
     */
    void deliver(RenderedMail mail, MailMessage message);
}
