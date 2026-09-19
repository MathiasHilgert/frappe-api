package com.frappe.platform.mail;

/**
 * Sends one transactional email, rendered from a template in exactly one language: the message's locale, or its
 * fallback locale as a whole when the template's text is incomplete in the requested one.
 *
 * <p>Call it only from an event listener that runs after the commit ({@code @ApplicationModuleListener}): mail cannot be
 * rolled back, and a failure fails the listener, so the outbox retries it. Delivery is therefore at least once; pass an
 * {@linkplain MailMessage#withIdempotencyKey idempotency key} whenever a retry would send identical content.
 *
 * <p>Events carry ids only, never an address or a one-time code. A code reaches the mail in memory through an SPI of the
 * module that owns it, called from that module's listener; notification implements identity's {@code CodeMailer}:
 *
 * <pre>{@code
 * @Override
 * public void send(CodeMail mail) {
 *     mailer.send(MailMessage.of("notification/sign-up-code", mail.recipient(), mail.locale(),
 *             SupportedLocales.FALLBACK, Map.of("code", mail.code(), "minutes", mail.validFor().toMinutes())));
 *     // no idempotency key: every attempt carries a fresh code
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
