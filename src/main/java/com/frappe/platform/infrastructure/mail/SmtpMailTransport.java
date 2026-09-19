package com.frappe.platform.infrastructure.mail;

import com.frappe.platform.mail.MailDeliveryException;
import com.frappe.platform.mail.MailMessage;
import jakarta.mail.MessagingException;
import java.nio.charset.StandardCharsets;
import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.List;
import java.util.OptionalInt;
import org.eclipse.angus.mail.smtp.SMTPAddressFailedException;
import org.springframework.mail.MailException;
import org.springframework.mail.MailSendException;
import org.springframework.mail.javamail.JavaMailSender;
import org.springframework.mail.javamail.MimeMessageHelper;

/**
 * Delivers over SMTP (Spring Mail), in the {@code local} profile to Mailpit, as HTML with its text alternative. SMTP has
 * no idempotency key; Mailpit shows every attempt. A recipient refused with a {@code 5xx} reply is a
 * {@link MailRejectedException}; anything else (a {@code 4xx} deferral, another {@code 5xx} such as {@code 530}, an
 * unreachable server) is a {@link MailDeliveryException}, retried by the outbox.
 */
final class SmtpMailTransport implements MailTransport {

    private final JavaMailSender sender;

    private final String from;

    /**
     * Creates the transport.
     *
     * @param sender Boot's mail sender ({@code spring.mail.*})
     * @param from the sender, for example {@code Frappé <no-reply@example.com>}
     */
    SmtpMailTransport(JavaMailSender sender, String from) {
        this.sender = sender;
        this.from = from;
    }

    @Override
    public String provider() {
        return "smtp";
    }

    @Override
    public void deliver(RenderedMail mail, MailMessage message) {
        try {
            var mime = sender.createMimeMessage();
            var helper = new MimeMessageHelper(mime, true, StandardCharsets.UTF_8.name());
            helper.setFrom(from);
            helper.setTo(message.recipient());
            helper.setSubject(mail.subject());
            helper.setText(mail.text(), mail.html());
            sender.send(mime);
        } catch (MailException | MessagingException e) {
            // No cause: SMTP replies and Spring's failed-message list quote the recipient, and the cause chain reaches
            // logs. The reply code or the exception type is enough to tell a refusal from an outage.
            var refusedRecipient = permanentRecipientRefusal(e);
            if (refusedRecipient.isPresent()) {
                throw new MailRejectedException("SMTP server rejected mail %s (reply %d)"
                        .formatted(message.templateId(), refusedRecipient.getAsInt()));
            }
            throw new MailDeliveryException("Sending mail %s over SMTP failed (%s)"
                    .formatted(message.templateId(), e.getClass().getSimpleName()));
        }
    }

    // Only a 5xx reply to RCPT TO (the recipient does not exist or is refused) is permanent, wherever Spring and Angus
    // Mail nested it (failed messages, next exceptions). Other 5xx replies (530 authentication required, sender not
    // permitted) are about our settings and stay transient, as with Resend.
    private static OptionalInt permanentRecipientRefusal(Exception failure) {
        return causesOf(failure).stream()
                .filter(SMTPAddressFailedException.class::isInstance)
                .mapToInt(cause -> ((SMTPAddressFailedException) cause).getReturnCode())
                .filter(code -> code >= 500 && code < 600)
                .findFirst();
    }

    private static List<Throwable> causesOf(Exception failure) {
        var found = new ArrayList<Throwable>();
        var pending = new ArrayDeque<Throwable>();
        pending.add(failure);
        if (failure instanceof MailSendException send) {
            pending.addAll(send.getFailedMessages().values());
        }
        while (!pending.isEmpty()) {
            var cause = pending.poll();
            if (!found.contains(cause)) {
                found.add(cause);
                if (cause.getCause() != null) {
                    pending.add(cause.getCause());
                }
            }
        }
        return found;
    }
}
