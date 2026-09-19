package com.frappe.platform.infrastructure.mail;

import com.frappe.platform.mail.Mailer;
import com.resend.Resend;
import gg.jte.ContentType;
import gg.jte.TemplateEngine;
import io.micrometer.core.instrument.MeterRegistry;
import io.micrometer.observation.ObservationRegistry;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.boot.context.properties.EnableConfigurationProperties;
import org.springframework.context.MessageSource;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.mail.javamail.JavaMailSender;

/**
 * Wires the {@link Mailer}: precompiled JTE templates, the application message source, and one transport chosen by
 * {@code frappe.mail.provider} (Resend by default, SMTP in the {@code local} profile).
 */
@Configuration(proxyBeanMethods = false)
@EnableConfigurationProperties(MailDeliveryProperties.class)
class MailConfiguration {

    private static final String PROVIDER = "frappe.mail.provider";

    /** Creates the configuration; instantiated by Spring. */
    MailConfiguration() {}

    /**
     * The renderer over the templates JTE generated at build time (never compiled at runtime).
     *
     * @param messageSource the application message source
     * @param meters where locale fallbacks are counted
     * @return the renderer
     */
    @Bean
    MailRenderer mailRenderer(MessageSource messageSource, MeterRegistry meters) {
        return new MailRenderer(TemplateEngine.createPrecompiled(ContentType.Html), messageSource, meters);
    }

    /**
     * The port modules send mail through.
     *
     * @param renderer the renderer
     * @param transport the configured provider
     * @param observations where {@code mail.send} is recorded
     * @return the mailer
     */
    @Bean
    Mailer mailer(MailRenderer renderer, MailTransport transport, ObservationRegistry observations) {
        return new ObservedMailer(renderer, transport, observations);
    }

    /**
     * Resend's SDK call, authenticated with {@code RESEND_API_KEY}.
     *
     * @param properties the mail settings
     * @return the send call
     */
    @Bean
    @ConditionalOnProperty(name = PROVIDER, havingValue = "resend", matchIfMissing = true)
    ResendEmails resendEmails(MailDeliveryProperties properties) {
        var emails = new Resend(properties.resend().apiKey()).emails();
        return emails::send;
    }

    /**
     * Delivery through Resend, the default outside the {@code local} profile.
     *
     * @param emails Resend's send call
     * @param properties the mail settings
     * @return the transport
     */
    @Bean
    @ConditionalOnProperty(name = PROVIDER, havingValue = "resend", matchIfMissing = true)
    MailTransport resendMailTransport(ResendEmails emails, MailDeliveryProperties properties) {
        return new ResendMailTransport(emails, properties.from());
    }

    /**
     * Delivery over SMTP ({@code spring.mail.*}), Mailpit in the {@code local} profile.
     *
     * @param sender Boot's mail sender
     * @param properties the mail settings
     * @return the transport
     */
    @Bean
    @ConditionalOnProperty(name = PROVIDER, havingValue = "smtp")
    MailTransport smtpMailTransport(JavaMailSender sender, MailDeliveryProperties properties) {
        return new SmtpMailTransport(sender, properties.from());
    }
}
