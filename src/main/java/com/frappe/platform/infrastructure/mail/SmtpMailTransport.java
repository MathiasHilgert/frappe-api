package com.frappe.platform.infrastructure.mail;

import com.frappe.platform.mail.MailDeliveryException;
import com.frappe.platform.mail.MailMessage;
import jakarta.mail.MessagingException;
import jakarta.mail.internet.AddressException;
import java.nio.charset.StandardCharsets;
import java.util.ArrayDeque;
import java.util.OptionalInt;
import org.eclipse.angus.mail.smtp.SMTPAddressFailedException;
import org.eclipse.angus.mail.smtp.SMTPSendFailedException;
import org.springframework.mail.MailException;
import org.springframework.mail.MailSendException;
import org.springframework.mail.javamail.JavaMailSender;
import org.springframework.mail.javamail.MimeMessageHelper;

/**
 * Delivers over SMTP (Spring Mail), in the {@code local} profile to Mailpit, as HTML with its text alternative. SMTP has
 * no idempotency key; Mailpit shows every attempt. A permanent refusal (a {@code 5xx} reply, a malformed address) is a
 * {@link MailRejectedException}; anything else, a {@code 4xx} deferral or an unreachable server, is a
 * {@link MailDeliveryException}, retried by the outbox.
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
            var permanentReply = permanentReplyCode(e);
            if (permanentReply.isPresent()) {
                throw new MailRejectedException("SMTP server rejected mail %s (reply %d)"
                        .formatted(message.templateId(), permanentReply.getAsInt()));
            }
            if (hasMalformedAddress(e)) {
                throw new MailRejectedException("Mail %s has a malformed address".formatted(message.templateId()));
            }
            throw new MailDeliveryException("Sending mail %s over SMTP failed (%s)"
                    .formatted(message.templateId(), e.getClass().getSimpleName()));
        }
    }

    // The 5xx reply to RCPT TO or DATA, wherever Spring and Angus Mail nested it (failed messages, next exceptions).
    private static OptionalInt permanentReplyCode(Exception failure) {
        for (var cause : causesOf(failure)) {
            var code =
                    switch (cause) {
                        case SMTPAddressFailedException refused -> refused.getReturnCode();
                        case SMTPSendFailedException refused -> refused.getReturnCode();
                        default -> 0;
                    };
            if (code >= 500 && code < 600) {
                return OptionalInt.of(code);
            }
        }
        return OptionalInt.empty();
    }

    private static boolean hasMalformedAddress(Exception failure) {
        return causesOf(failure).stream()
                .anyMatch(cause -> cause instanceof AddressException && !(cause instanceof SMTPAddressFailedException));
    }

    private static java.util.List<Throwable> causesOf(Exception failure) {
        var found = new java.util.ArrayList<Throwable>();
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
