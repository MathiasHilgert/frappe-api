package com.frappe.platform.mail;

/**
 * Sends one transactional email, rendered from a template in exactly one language: the message's locale, or its
 * fallback locale as a whole when the template's text is incomplete in the requested one.
 *
 * <p>Call it only from an event listener that runs after the commit ({@code @ApplicationModuleListener}): mail cannot be
 * rolled back, and a failure fails the listener, so the outbox retries it. Delivery is therefore at least once; pass an
 * {@linkplain MailMessage#withIdempotencyKey idempotency key} whenever a retry would send identical content.
 *
 * <pre>{@code
 * @ApplicationModuleListener
 * void on(EmailProofRequested event) {
 *     mailer.send(MailMessage.of("identity/email-proof", event.email(), event.locale(), event.tenantLocale(),
 *                     Map.of("code", event.code()))
 *             .withIdempotencyKey("email-proof/" + event.eventId()));
 * }
 * }</pre>
 */
public interface Mailer {

    /**
     * Renders and sends a message.
     *
     * @param message the message to send
     * @throws MailDeliveryException if delivery failed for a reason a retry can fix (provider outage, rate limit,
     *     settings to correct); the calling listener fails and the outbox retries it. A permanent rejection (an
     *     invalid or refused recipient) does not throw: it is logged once, counted as {@code mail.outcome=rejected}
     *     and not retried
     */
    void send(MailMessage message);
}
