package com.frappe.platform.infrastructure.mail;

import com.frappe.platform.mail.MailMessage;
import com.frappe.platform.mail.Mailer;
import io.micrometer.observation.Observation;
import io.micrometer.observation.ObservationRegistry;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

/**
 * The {@link Mailer}: renders the message in one language and hands it to the configured transport inside one
 * {@code mail.send} observation (span and timer, tagged {@code mail.provider}, {@code mail.template}, {@code mail.locale},
 * {@code mail.outcome} = {@code sent} | {@code rejected} | {@code failed}, and {@code error}). A permanent rejection is
 * logged once here and not retried; a transient failure is rethrown for the outbox to retry. Neither the observation
 * nor the logs ever carry the recipient, the subject or the body.
 */
final class ObservedMailer implements Mailer {

    /** Observation name; the timer is exported as {@code mail.send}. */
    static final String OBSERVATION = "mail.send";

    private static final Logger log = LoggerFactory.getLogger(ObservedMailer.class);

    private static final String NO_LOCALE = "none";

    private static final String OUTCOME = "mail.outcome";

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
        // Every tag is set up front (mail.locale "none" until rendered, mail.outcome "failed" until known), so the
        // timer keeps one tag set.
        var observation = Observation.createNotStarted(OBSERVATION, observations)
                .lowCardinalityKeyValue("mail.provider", transport.provider())
                .lowCardinalityKeyValue("mail.template", message.templateId())
                .lowCardinalityKeyValue("mail.locale", NO_LOCALE)
                .lowCardinalityKeyValue(OUTCOME, "failed");
        // A transient failure (MailDeliveryException) is recorded on the observation and rethrown, not logged: the
        // listener fails, its boundary logs it once, and the outbox retries it.
        observation.observe(() -> {
            var mail = renderer.render(message);
            var locale = mail.locale().toLanguageTag();
            observation.lowCardinalityKeyValue("mail.locale", locale);
            try {
                transport.deliver(mail, message);
                observation.lowCardinalityKeyValue(OUTCOME, "sent");
            } catch (MailRejectedException rejected) {
                // Handled here: retrying cannot succeed, so the listener completes and this is the one log line.
                observation.lowCardinalityKeyValue(OUTCOME, "rejected");
                log.atError()
                        .addKeyValue(LogFields.TEMPLATE, message.templateId())
                        .addKeyValue(LogFields.PROVIDER, transport.provider())
                        .addKeyValue(LogFields.LOCALE, locale)
                        .setCause(rejected)
                        .log("Mail {} was rejected for good and is not retried", message.templateId());
            }
        });
    }
}
