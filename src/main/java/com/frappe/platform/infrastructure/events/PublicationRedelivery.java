package com.frappe.platform.infrastructure.events;

import java.time.Instant;
import java.util.Collection;
import java.util.Optional;
import java.util.function.Supplier;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.context.ApplicationEvent;
import org.springframework.context.ApplicationListener;
import org.springframework.context.PayloadApplicationEvent;
import org.springframework.modulith.events.core.EventPublicationRepository;
import org.springframework.modulith.events.core.EventSerializer;
import org.springframework.transaction.event.TransactionalApplicationListener;
import org.springframework.util.ClassUtils;
import tools.jackson.core.JacksonException;

/**
 * Resubmits one failed publication the way Spring Modulith 2.1.1 does ({@code DefaultEventPublicationRegistry
 * .processPublications} and {@code PersistentApplicationEventMulticaster.invokeTargetListener}), for a row the recovery
 * job loaded itself. Modulith offers no public API to resubmit a chosen, bounded set of publications: {@code
 * FailedEventPublications.resubmit} limits before it filters, and {@code IncompleteEventPublications} reads every
 * incomplete row.
 *
 * <p>Mirrored Spring Modulith 2.1.1 behaviors, each pinned by {@code ModulithRegistryContractIntegrationTests} so an
 * upgrade that changes one fails the build:
 *
 * <ol>
 *   <li>Listener lookup: the row's {@code listener_id} equals {@link TransactionalApplicationListener#getListenerId()}
 *       of a listener the application context exposes ({@code invokeTargetListener} matches the same id).
 *   <li>Claim: {@link EventPublicationRepository#markResubmitted} succeeds once per attempt ({@code STATUS !=
 *       'RESUBMITTED'} guard), sets {@code last_resubmission_date} and increments {@code completion_attempts}.
 *   <li>Delivery: {@link TransactionalApplicationListener#processEvent} with the deserialized event wrapped in a
 *       {@link PayloadApplicationEvent}, as {@code executeListenerWithCompletion} does.
 *   <li>Completion without in-progress state: {@code EventPublicationRegistry.markCompleted(event, listener)} completes
 *       by serialized event and listener id and, in ARCHIVE mode, moves the row to the archive.
 * </ol>
 *
 * <p>One accepted difference: Modulith also registers the publication as in progress, which is internal API, so a
 * listener failing asynchronously (the NATS relay) does not mark the row FAILED; it stays RESUBMITTED until {@code
 * frappe.outbox.recovery.stuck-after} releases it for the next retry.
 */
class PublicationRedelivery {

    private static final Logger log = LoggerFactory.getLogger(PublicationRedelivery.class);

    /** What happened to a publication handed to {@link #redeliver}. */
    enum Outcome {

        /** Claimed and handed to its listener. */
        RESUBMITTED,

        /** Another instance claimed it first; nothing was done. */
        CLAIMED_ELSEWHERE,

        /** The event class is not on the classpath; not claimed. */
        UNKNOWN_EVENT_TYPE,

        /** The stored JSON does not deserialize into the event class; not claimed. */
        UNREADABLE_PAYLOAD,

        /** No listener with the stored id exists in this application; not claimed. */
        UNKNOWN_LISTENER,

        /** The listener threw synchronously; the publication was marked failed again. */
        LISTENER_FAILED
    }

    private final EventPublicationRepository repository;
    private final EventSerializer serializer;
    private final Supplier<Collection<ApplicationListener<?>>> listeners;
    private final ClassLoader classLoader;

    /**
     * Creates the redelivery.
     *
     * @param repository the registry's repository, for the guarded state transitions
     * @param serializer the registry's event serializer
     * @param listeners the application's listeners, looked up on every call
     * @param classLoader loads event classes
     */
    PublicationRedelivery(
            EventPublicationRepository repository,
            EventSerializer serializer,
            Supplier<Collection<ApplicationListener<?>>> listeners,
            ClassLoader classLoader) {
        this.repository = repository;
        this.serializer = serializer;
        this.listeners = listeners;
        this.classLoader = classLoader;
    }

    /**
     * Resubmits the publication to its listener unless it cannot be delivered or another instance claimed it.
     * Everything that could make delivery impossible is checked before claiming, so such a row stays FAILED and can be
     * dead-lettered.
     *
     * @param publication the loaded row
     * @param now the resubmission time
     * @return what happened
     */
    Outcome redeliver(FailedPublication publication, Instant now) {
        Class<?> eventType;
        try {
            eventType = ClassUtils.forName(publication.eventType(), classLoader);
        } catch (ClassNotFoundException | LinkageError e) {
            return Outcome.UNKNOWN_EVENT_TYPE;
        }
        Object event;
        try {
            event = serializer.deserialize(publication.serializedEvent(), eventType);
        } catch (JacksonException e) {
            // Modulith 2.1.1's JacksonEventSerializer calls ObjectReader.readValue without wrapping; Jackson 3 throws
            // JacksonException (StreamReadException, DatabindException) for input that does not fit the class.
            return Outcome.UNREADABLE_PAYLOAD;
        }
        var listener = listenerFor(publication.listenerId());
        if (listener.isEmpty()) {
            return Outcome.UNKNOWN_LISTENER;
        }
        if (!repository.markResubmitted(publication.id(), now)) {
            return Outcome.CLAIMED_ELSEWHERE;
        }
        try {
            listener.get().processEvent(new PayloadApplicationEvent<>(this, event));
            return Outcome.RESUBMITTED;
        } catch (RuntimeException e) {
            // The one broad catch: listener code is arbitrary, and one failing listener must not abort the batch.
            // Modulith's resubmission catches the same way. Logged here, the boundary that handles it.
            repository.markFailed(publication.id());
            log.atWarn()
                    .addKeyValue(LogFields.PUBLICATION_ID, publication.id())
                    .addKeyValue(LogFields.LISTENER_ID, publication.listenerId())
                    .setCause(e)
                    .log("Resubmitted event publication failed in its listener; it stays failed for retry");
            return Outcome.LISTENER_FAILED;
        }
    }

    @SuppressWarnings("unchecked")
    private Optional<TransactionalApplicationListener<ApplicationEvent>> listenerFor(String listenerId) {
        return listeners.get().stream()
                .filter(TransactionalApplicationListener.class::isInstance)
                .map(listener -> (TransactionalApplicationListener<ApplicationEvent>) listener)
                .filter(listener -> listenerId.equals(listener.getListenerId()))
                .findFirst();
    }
}
