package com.frappe.platform.infrastructure.mail;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatExceptionOfType;

import com.frappe.platform.mail.MailDeliveryException;
import com.frappe.platform.mail.MailMessage;
import jakarta.mail.MessagingException;
import jakarta.mail.SendFailedException;
import jakarta.mail.internet.InternetAddress;
import jakarta.mail.internet.MimeMessage;
import jakarta.mail.internet.MimeMultipart;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import org.eclipse.angus.mail.smtp.SMTPAddressFailedException;
import org.eclipse.angus.mail.smtp.SMTPSendFailedException;
import org.junit.jupiter.api.Test;
import org.springframework.mail.MailSendException;
import org.springframework.mail.javamail.JavaMailSenderImpl;

class SmtpMailTransportTest {

    private static final String RECIPIENT = "ana.maria@example.com";

    private final RenderedMail mail = new RenderedMail(
            "Tu código", "<html><p>Tu código es 123456</p></html>", "Tu código es 123456", Locale.of("es"));

    private final MailMessage message =
            MailMessage.of("mailprobe/welcome", RECIPIENT, Locale.of("es"), Locale.of("en"), Map.of());

    @Test
    void sendsHtmlWithAPlainTextAlternative() throws Exception {
        // Given
        var sent = new ArrayList<MimeMessage>();
        var transport = new SmtpMailTransport(sender(sent::add), "Frappé <no-reply@frappe.test>");

        // When
        transport.deliver(mail, message);

        // Then
        var mime = sent.getFirst();
        mime.saveChanges();
        assertThat(mime.getSubject()).isEqualTo("Tu código");
        var alternative = alternativeOf(mime);
        assertThat(alternative.getContentType()).startsWith("multipart/alternative");
        assertThat(alternative.getBodyPart(0).getContentType()).startsWith("text/plain");
        assertThat(alternative.getBodyPart(0).getContent()).isEqualTo("Tu código es 123456");
        assertThat(alternative.getBodyPart(1).getContentType()).startsWith("text/html");
    }

    @Test
    void aRecipientTheServerRefusesPermanentlyIsARejection() throws Exception {
        // Given the server answers 550 to RCPT TO
        var refused = new SMTPAddressFailedException(
                new InternetAddress(RECIPIENT), "RCPT TO", 550, "550 5.1.1 <" + RECIPIENT + ">: mailbox unavailable");
        var transport = failingWith(new SendFailedException("Invalid Addresses", refused));

        // When / Then
        assertThatExceptionOfType(MailRejectedException.class)
                .isThrownBy(() -> transport.deliver(mail, message))
                .withMessageContaining("550")
                .withMessageNotContaining("ana.maria")
                .withNoCause();
    }

    @Test
    void aTemporaryRefusalIsTransient() throws Exception {
        // Given the server answers 451 (try again later)
        var deferred = new SMTPAddressFailedException(
                new InternetAddress(RECIPIENT), "RCPT TO", 451, "451 4.7.1 <" + RECIPIENT + ">: try again later");
        var transport = failingWith(new SendFailedException("Invalid Addresses", deferred));

        // When / Then
        assertThatExceptionOfType(MailDeliveryException.class)
                .isThrownBy(() -> transport.deliver(mail, message))
                .isNotInstanceOf(MailRejectedException.class)
                .withMessageNotContaining("ana.maria")
                .withNoCause();
    }

    @Test
    void aPermanentReplyAboutTheSenderOrTheSessionIsTransient() throws Exception {
        // Given the server answers 530 (authentication required): a setting to fix, not a bad recipient
        var transport = failingWith(new SMTPSendFailedException(
                "MAIL FROM", 530, "530 5.7.0 Authentication required", null, null, null, null));

        // When / Then
        assertThatExceptionOfType(MailDeliveryException.class)
                .isThrownBy(() -> transport.deliver(mail, message))
                .isNotInstanceOf(MailRejectedException.class);
    }

    @Test
    void anUnreachableServerIsTransient() {
        // Given
        var smtp = new JavaMailSenderImpl();
        smtp.setHost("localhost");
        smtp.setPort(TestSockets.closedPort());
        var transport = new SmtpMailTransport(smtp, "Frappé <no-reply@frappe.test>");

        // When / Then
        assertThatExceptionOfType(MailDeliveryException.class)
                .isThrownBy(() -> transport.deliver(mail, message))
                .isNotInstanceOf(MailRejectedException.class);
    }

    private static SmtpMailTransport failingWith(MessagingException failure) {
        return new SmtpMailTransport(
                sender(mime -> {
                    throw new MailSendException(java.util.Map.of(mime, failure));
                }),
                "Frappé <no-reply@frappe.test>");
    }

    private static JavaMailSenderImpl sender(java.util.function.Consumer<MimeMessage> onSend) {
        return new JavaMailSenderImpl() {
            @Override
            protected void doSend(MimeMessage[] mimeMessages, Object[] originalMessages) {
                List.of(mimeMessages).forEach(onSend);
            }
        };
    }

    // MimeMessageHelper's MULTIPART_MODE_MIXED_RELATED nests the alternative inside mixed > related.
    private static MimeMultipart alternativeOf(MimeMessage mime) throws Exception {
        var part = (MimeMultipart) mime.getContent();
        while (!part.getContentType().startsWith("multipart/alternative")) {
            part = (MimeMultipart) part.getBodyPart(0).getContent();
        }
        return part;
    }
}
