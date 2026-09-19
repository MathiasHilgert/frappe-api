package com.frappe.platform.infrastructure.metrics;

import java.util.List;

/** A business metric declaration breaks the metric rules; thrown at startup so the application refuses to start. */
class InvalidBusinessMetricException extends RuntimeException {

    private static final long serialVersionUID = 1L;

    /**
     * Creates the exception listing every problem of one event type.
     *
     * @param eventType the event the metrics are declared on
     * @param problems what is wrong, each with the fix
     */
    InvalidBusinessMetricException(Class<?> eventType, List<String> problems) {
        super("Invalid business metrics on " + eventType.getName() + ":\n  - " + String.join("\n  - ", problems));
    }

    /**
     * Creates the exception for a problem spanning several declarations.
     *
     * @param message what is wrong and how to fix it
     */
    InvalidBusinessMetricException(String message) {
        super(message);
    }
}
