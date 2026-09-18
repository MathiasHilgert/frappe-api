package com.frappe.platform.infrastructure.metrics;

import com.frappe.platform.MetricUnit;
import java.util.List;
import java.util.function.Function;

/**
 * A validated business metric of one event type, ready to record.
 *
 * @param eventType the domain event it is recorded from
 * @param kind counter or distribution
 * @param name full metric name, {@code frappe.<module>.<name>}
 * @param description what one recording means
 * @param unit base unit of a distribution; {@code null} for counters
 * @param tags dimensions read from the event
 * @param value reads the recorded value of a distribution; {@code null} for counters
 */
record BusinessMetric(
        Class<?> eventType,
        Kind kind,
        String name,
        String description,
        MetricUnit unit,
        List<TagSource> tags,
        ValueSource value) {

    /** How the metric is recorded. */
    enum Kind {
        /** One increment per event. */
        COUNTER,
        /** One sample per event. */
        DISTRIBUTION
    }

    /**
     * One dimension and how to read its value from an event.
     *
     * @param key tag key
     * @param value reads the tag value; throws {@link MetricRecordingException} if it is missing
     */
    record TagSource(String key, Function<Object, String> value) {}

    /** Reads the value a distribution records from an event. */
    @FunctionalInterface
    interface ValueSource {

        /**
         * Reads the value.
         *
         * @param event the event
         * @return the value in the metric's base unit
         * @throws MetricRecordingException if the value is missing or cannot be read
         */
        Measurement read(Object event);
    }

    /**
     * A value to record.
     *
     * @param amount the value in the metric's base unit
     * @param currency ISO 4217 code for money, {@code null} otherwise
     */
    record Measurement(double amount, String currency) {}
}
