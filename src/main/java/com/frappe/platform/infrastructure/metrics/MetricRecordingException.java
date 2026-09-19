package com.frappe.platform.infrastructure.metrics;

/** A value or tag of a business metric could not be read from an event; the metric is skipped for that event. */
class MetricRecordingException extends RuntimeException {

    private static final long serialVersionUID = 1L;

    /**
     * Creates the exception for a missing value.
     *
     * @param message which field is missing on which event
     */
    MetricRecordingException(String message) {
        super(message);
    }

    /**
     * Creates the exception for a field that could not be read.
     *
     * @param message which field of which event
     * @param cause the reflection failure
     */
    MetricRecordingException(String message, Throwable cause) {
        super(message, cause);
    }
}
