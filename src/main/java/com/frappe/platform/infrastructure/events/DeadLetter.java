package com.frappe.platform.infrastructure.events;

import java.util.UUID;

/**
 * A publication moved to {@code platform.event_publication_dead_letter}.
 *
 * @param publicationId id of the publication (not the event id)
 * @param eventType fully qualified class name of the event
 * @param listenerId the listener that never completed it
 * @param completionAttempts attempts made before giving up
 * @param reason why it was given up
 */
record DeadLetter(
        UUID publicationId, String eventType, String listenerId, int completionAttempts, DeadLetterReason reason) {}
