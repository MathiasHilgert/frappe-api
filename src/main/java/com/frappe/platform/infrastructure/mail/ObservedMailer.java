package com.frappe.platform.infrastructure.mail;

import com.frappe.platform.mail.MailDeliveryException;
import com.frappe.platform.mail.MailMessage;
import com.frappe.platform.mail.Mailer;
import io.micrometer.observation.Observation;
import io.micrometer.observation.ObservationRegistry;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

/**
 * The {@link Mailer}: renders the message in one language and hands it to the configured transport inside one
 * {@code mail.send} observation (span and timer, tagged {@code mail.provider}, {@code mail.template},
 * {@code mail.locale} and {@code error}). The timer's {@code error} tag counts failed deliveries. Neither the
 * observation nor the logs ever carry the recipient, the subject or the body.
 */
final class ObservedMailer implements Mailer {

    /** Observation name; the timer is exported as {@code mail.send}. */
    static final String OBSERVATION = "mail.send";

    private static final Logger log = LoggerFactory.getLogger(ObservedMailer.class);

    private static final String NO_LOCALE = "none";

    private final MailRenderer renderer;

    private final MailTransport transport;

    private final ObservationRegistry observations;

    /**
     * Creates the mailer.
     *
     * @param renderer renders templates in one language
     * @param transport the provider
     * @param observations where {@code mail.send} is recorded
     */
    ObservedMailer(MailRenderer renderer, MailTransport transport, ObservationRegistry observations) {
        this.renderer = renderer;
        this.transport = transport;
        this.observations = observations;
    }

    @Override
    public void send(MailMessage message) {
        // Every tag is set up front (mail.locale as "none" until rendered), so the timer keeps one tag set.
        var observation = Observation.createNotStarted(OBSERVATION, observations)
                .lowCardinalityKeyValue("mail.provider", transport.provider())
                .lowCardinalityKeyValue("mail.template", message.templateId())
                .lowCardinalityKeyValue("mail.locale", NO_LOCALE);
        var locale = new String[] {NO_LOCALE};
        try {
            observation.observe(() -> {
                var mail = renderer.render(message);
                locale[0] = mail.locale().toLanguageTag();
                observation.lowCardinalityKeyValue("mail.locale", locale[0]);
                transport.deliver(mail, message);
            });
        } catch (MailDeliveryException e) {
            // Logged here, once, because the provider boundary knows the context; the listener fails and the outbox
            // retries, so no caller logs it again.
            log.atError()
                    .addKeyValue(LogFields.TEMPLATE, message.templateId())
                    .addKeyValue(LogFields.PROVIDER, transport.provider())
                    .addKeyValue(LogFields.LOCALE, locale[0])
                    .setCause(e)
                    .log("Sending mail {} failed; the outbox retries the listener", message.templateId());
            throw e;
        }
        log.atDebug()
                .addKeyValue(LogFields.TEMPLATE, message.templateId())
                .addKeyValue(LogFields.PROVIDER, transport.provider())
                .addKeyValue(LogFields.LOCALE, locale[0])
                .log("Sent mail {}", message.templateId());
    }
}
