package com.frappe.platform.infrastructure.events;

/** Why the recovery job gave up on a publication; stored in {@code event_publication_dead_letter.reason}. */
enum DeadLetterReason {

    /** Every allowed attempt failed ({@code frappe.outbox.recovery.max-attempts}). */
    MAX_ATTEMPTS_EXHAUSTED,

    /** The event class is no longer on the classpath (renamed or deleted); the registry cannot load the row. */
    UNKNOWN_EVENT_TYPE,

    /** The stored JSON no longer deserializes into the event class (an incompatible change of the event). */
    UNREADABLE_PAYLOAD
}
