package com.frappe.platform.infrastructure.events;

/** Why the recovery job gave up on a publication; stored in {@code event_publication_dead_letter.reason}. */
enum DeadLetterReason {

    /** Every allowed attempt failed ({@code frappe.outbox.recovery.max-attempts}). */
    MAX_ATTEMPTS_EXHAUSTED
}
