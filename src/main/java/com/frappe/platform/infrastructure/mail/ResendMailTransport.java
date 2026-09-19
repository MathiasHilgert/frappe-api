package com.frappe.platform.infrastructure.mail;

import com.frappe.platform.mail.MailDeliveryException;
import com.frappe.platform.mail.MailMessage;
import com.resend.core.exception.ResendException;
import com.resend.core.net.RequestOptions;
import com.resend.services.emails.model.CreateEmailOptions;
import java.io.IOException;
import java.util.Set;

/**
 * Delivers through Resend's API (HTML and its text alternative), passing the message's idempotency key so a retry
 * within 24 hours is not resent. A request Resend will never accept is a {@link MailRejectedException}; outages,
 * limits and fixable settings are a {@link MailDeliveryException}, retried by the outbox.
 */
final class ResendMailTransport implements MailTransport {

    private static final Set<Integer> TRANSIENT_CLIENT_ERRORS = Set.of(401, 403, 408, 409, 429);

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
                .text(mail.text())
                .build();
        var request = RequestOptions.builder();
        message.idempotencyKey().ifPresent(request::setIdempotencyKey);
        try {
            emails.send(options, request.build());
        } catch (ResendException e) {
            // No cause: Resend's error text may echo the request (the recipient), and the cause chain reaches logs.
            var failure = "HTTP %s (%s)".formatted(e.getStatusCode(), e.getErrorName());
            if (isPermanent(e.getStatusCode())) {
                throw new MailRejectedException("Resend rejected mail %s: %s".formatted(message.templateId(), failure));
            }
            throw new MailDeliveryException(
                    "Sending mail %s through Resend failed: %s".formatted(message.templateId(), failure));
        } catch (RuntimeException e) {
            // The SDK wraps network failures (IOException) in a bare RuntimeException; anything else is a bug.
            if (e.getCause() instanceof IOException unreachable) {
                throw new MailDeliveryException(
                        "Sending mail %s through Resend failed: Resend could not be reached"
                                .formatted(message.templateId()),
                        unreachable);
            }
            throw e;
        }
    }

    // A 4xx means this request will never be accepted (validation, invalid recipient), except for the ones that pass
    // or that an operator fixes without the mail being lost: 401/403 (API key, unverified domain), 408 (timeout), 409
    // (concurrent request with the same idempotency key) and 429 (rate limit). Those, 5xx and unknown stay transient.
    private static boolean isPermanent(Integer status) {
        return status != null && status >= 400 && status < 500 && !TRANSIENT_CLIENT_ERRORS.contains(status);
    }
}
