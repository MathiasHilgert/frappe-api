package com.frappe.platform.infrastructure.events;

import java.util.UUID;

/**
 * A failed publication due for another attempt, as stored in {@code platform.event_publication}.
 *
 * @param id publication id (not the event id)
 * @param listenerId the listener it targets
 * @param eventType fully qualified class name of the event
 * @param serializedEvent the event as stored by the registry's serializer
 */
record FailedPublication(UUID id, String listenerId, String eventType, String serializedEvent) {}
