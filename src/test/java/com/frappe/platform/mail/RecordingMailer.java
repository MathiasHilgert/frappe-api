package com.frappe.platform.mail;

import java.util.List;
import java.util.concurrent.CopyOnWriteArrayList;
import java.util.concurrent.atomic.AtomicBoolean;

/**
 * A {@link Mailer} for module tests: records what would be sent instead of rendering and delivering it. Register it as a
 * {@code @Primary} bean of the test's configuration, publish the event, and assert on {@link #sent()}. Thread-safe:
 * mail listeners run asynchronously.
 *
 * <pre>{@code
 * @Bean @Primary RecordingMailer recordingMailer() { return new RecordingMailer(); }
 * ...
 * await().until(() -> !mailer.sentTo("ana@example.com").isEmpty());
 * }</pre>
 */
public final class RecordingMailer implements Mailer {

    private final List<MailMessage> sent = new CopyOnWriteArrayList<>();

    private final AtomicBoolean failNext = new AtomicBoolean();

    /**
     * Records the message, or fails once like a provider outage after {@link #failNextSend()}.
     *
     * @param message the message
     * @throws MailDeliveryException once after {@link #failNextSend()}; nothing is recorded then
     */
    @Override
    public void send(MailMessage message) {
        if (failNext.compareAndSet(true, false)) {
            throw new MailDeliveryException("Recording mailer: simulated outage for " + message.templateId());
        }
        sent.add(message);
    }

    /** Makes the next {@link #send} fail with {@link MailDeliveryException}, as a transient provider failure would. */
    public void failNextSend() {
        failNext.set(true);
    }

    /**
     * Returns the messages sent so far.
     *
     * @return the messages, in sending order
     */
    public List<MailMessage> sent() {
        return List.copyOf(sent);
    }

    /**
     * Returns the messages sent to one recipient.
     *
     * @param recipient the address
     * @return those messages, in sending order
     */
    public List<MailMessage> sentTo(String recipient) {
        return sent.stream()
                .filter(message -> message.recipient().equals(recipient))
                .toList();
    }

    /** Forgets every recorded message. */
    public void clear() {
        sent.clear();
    }
}
