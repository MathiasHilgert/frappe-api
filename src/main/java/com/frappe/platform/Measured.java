package com.frappe.platform;

import java.lang.annotation.Documented;
import java.lang.annotation.ElementType;
import java.lang.annotation.Repeatable;
import java.lang.annotation.Retention;
import java.lang.annotation.RetentionPolicy;
import java.lang.annotation.Target;

/**
 * Declares a business distribution on a domain event record: every occurrence published in a committed transaction
 * records the value of one of its fields in {@code frappe.<module>.<name>} (count, sum and max; histogram buckets for
 * durations). Money is recorded in minor units with a {@code currency} tag.
 *
 * <pre>{@code
 * @Measured(name = "tabs.revenue", description = "Revenue of closed tabs", value = "total", unit = MetricUnit.MONEY)
 * public record TabClosed(..., Money total) implements DomainEvent {}
 * }</pre>
 */
@Documented
@Retention(RetentionPolicy.RUNTIME)
@Target(ElementType.TYPE)
@Repeatable(Measured.List.class)
public @interface Measured {

    /**
     * Metric name below the module prefix: lowercase words separated by dots, without {@code frappe.}.
     *
     * @return the metric name
     */
    String name();

    /**
     * What one recorded value means, shown on dashboards.
     *
     * @return the description
     */
    String description();

    /**
     * Name of the event field holding the value: a number, a {@code Duration} or money.
     *
     * @return the field name
     */
    String value();

    /**
     * Unit of the value; must match the field type ({@link MetricUnit#SECONDS} for durations, {@link MetricUnit#MONEY}
     * for money).
     *
     * @return the unit
     */
    MetricUnit unit();

    /**
     * Low-cardinality dimensions read from enum or boolean fields of the event.
     *
     * @return the tags
     */
    MetricTag[] tags() default {};

    /** Holds several {@link Measured} declarations on one event. */
    @Documented
    @Retention(RetentionPolicy.RUNTIME)
    @Target(ElementType.TYPE)
    @interface List {

        /**
         * The repeated declarations.
         *
         * @return the declarations
         */
        Measured[] value();
    }
}
