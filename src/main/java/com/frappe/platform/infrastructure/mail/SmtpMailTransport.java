package com.frappe.platform.infrastructure.mail;

import com.frappe.platform.mail.MailDeliveryException;
import com.frappe.platform.mail.MailMessage;
import jakarta.mail.MessagingException;
import java.nio.charset.StandardCharsets;
import org.springframework.mail.MailException;
import org.springframework.mail.javamail.JavaMailSender;
import org.springframework.mail.javamail.MimeMessageHelper;

/**
 * Delivers over SMTP (Spring Mail), in the {@code local} profile to Mailpit. SMTP has no idempotency key; Mailpit shows
 * every attempt.
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
            var helper = new MimeMessageHelper(mime, StandardCharsets.UTF_8.name());
            helper.setFrom(from);
            helper.setTo(message.recipient());
            helper.setSubject(mail.subject());
            helper.setText(mail.html(), true);
            sender.send(mime);
        } catch (MailException | MessagingException e) {
            // No cause: SMTP replies and Spring's failed-message list quote the recipient, and the cause chain reaches
            // logs. The exception type is enough to tell a refused connection from a rejected message.
            throw new MailDeliveryException(
                    "SMTP delivery failed (%s)".formatted(e.getClass().getSimpleName()));
        }
    }
}
