package com.frappe.platform;

import java.util.UUID;

/**
 * Creates identifiers for aggregates and events. Injected wherever ids are born so the domain stays free of
 * infrastructure and tests can supply fixed ids.
 */
@FunctionalInterface
public interface IdGenerator {

    /**
     * Creates a new, unique, time-ordered identifier.
     *
     * @return a new UUIDv7
     */
    UUID newId();
}
