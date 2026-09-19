package com.frappe.platform;

import java.lang.annotation.Documented;
import java.lang.annotation.ElementType;
import java.lang.annotation.Repeatable;
import java.lang.annotation.Retention;
import java.lang.annotation.RetentionPolicy;
import java.lang.annotation.Target;

/**
 * Declares a business counter on a domain event record: every occurrence published in a committed transaction adds one
 * to {@code frappe.<module>.<name>}. Nothing else is needed; the platform discovers the declaration at startup and
 * refuses to start if it breaks the metric rules (naming, description, low-cardinality tags).
 *
 * <pre>{@code
 * @Counted(name = "tabs.closed", description = "Tabs closed", tags = @MetricTag(key = "channel", from = "channel"))
 * public record TabClosed(..., Channel channel) implements DomainEvent {}
 * }</pre>
 */
@Documented
@Retention(RetentionPolicy.RUNTIME)
@Target(ElementType.TYPE)
@Repeatable(Counted.List.class)
public @interface Counted {

    /**
     * Metric name below the module prefix: lowercase words separated by dots, without {@code frappe.} and without a
     * {@code total} suffix ({@code tabs.closed} becomes {@code frappe.<module>.tabs.closed}).
     *
     * @return the metric name
     */
    String name();

    /**
     * What one count means, shown on dashboards.
     *
     * @return the description
     */
    String description();

    /**
     * Low-cardinality dimensions read from enum or boolean fields of the event.
     *
     * @return the tags
     */
    MetricTag[] tags() default {};

    /** Holds several {@link Counted} declarations on one event. */
    @Documented
    @Retention(RetentionPolicy.RUNTIME)
    @Target(ElementType.TYPE)
    @interface List {

        /**
         * The repeated declarations.
         *
         * @return the declarations
         */
        Counted[] value();
    }
}
