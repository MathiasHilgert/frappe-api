package com.frappe.platform.infrastructure.mail;

import com.frappe.platform.mail.MailDeliveryException;
import com.frappe.platform.mail.MailMessage;
import com.resend.core.exception.ResendException;
import com.resend.core.net.RequestOptions;
import com.resend.services.emails.model.CreateEmailOptions;
import java.io.IOException;

/** Delivers through Resend's API, passing the message's idempotency key so a retry within 24 hours is not resent. */
final class ResendMailTransport implements MailTransport {

    private final ResendEmails emails;

    private final String from;

    /**
     * Creates the transport.
     *
     * @param emails the Resend SDK call
     * @param from the sender, for example {@code Frappé <no-reply@example.com>}
     */
    ResendMailTransport(ResendEmails emails, String from) {
        this.emails = emails;
        this.from = from;
    }

    @Override
    public String provider() {
        return "resend";
    }

    @Override
    public void deliver(RenderedMail mail, MailMessage message) {
        var options = CreateEmailOptions.builder()
                .from(from)
                .to(message.recipient())
                .subject(mail.subject())
                .html(mail.html())
                .build();
        var request = RequestOptions.builder();
        message.idempotencyKey().ifPresent(request::setIdempotencyKey);
        try {
            emails.send(options, request.build());
        } catch (ResendException e) {
            // No cause: Resend's error text may echo the request (the recipient), and the cause chain reaches logs.
            throw new MailDeliveryException(
                    "Resend refused the mail: HTTP %s (%s)".formatted(e.getStatusCode(), e.getErrorName()));
        } catch (RuntimeException e) {
            // The SDK wraps network failures (IOException) in a bare RuntimeException; anything else is a bug.
            if (e.getCause() instanceof IOException unreachable) {
                throw new MailDeliveryException("Resend could not be reached", unreachable);
            }
            throw e;
        }
    }
}
