package com.frappe.platform;

import java.util.function.Function;
import java.util.function.Predicate;
import java.util.function.ToDoubleFunction;

/**
 * Declares business metrics in code when the annotations ({@link Counted}, {@link Measured}) cannot express them, e.g.
 * a value computed from several fields. Same naming rules and guardrails as the annotations; used from a module's
 * infrastructure through a {@link BusinessMetricsDeclaration} bean.
 *
 * <pre>{@code
 * @Bean
 * BusinessMetricsDeclaration tabMetrics() {
 *     return metrics -> metrics.on(TabClosed.class)
 *             .count("tabs.split", "Tabs closed with a split bill")
 *             .tag("channel", TabClosed::channel);
 * }
 * }</pre>
 */
public interface BusinessMetrics {

    /**
     * Starts declaring metrics recorded when events of this type are published in a committed transaction.
     *
     * @param eventType the domain event record
     * @param <E> the event type
     * @return the declarations for this event
     */
    <E extends DomainEvent> EventMetrics<E> on(Class<E> eventType);

    /**
     * Metrics of one event type.
     *
     * @param <E> the event type
     */
    interface EventMetrics<E> {

        /**
         * Counts every occurrence as {@code frappe.<module>.<name>}.
         *
         * @param name metric name below the module prefix, lowercase and dotted
         * @param description what one count means
         * @return the declaration, to add tags
         */
        MetricDeclaration<E> count(String name, String description);

        /**
         * Records a value per occurrence as the distribution {@code frappe.<module>.<name>}. Money is declared with
         * {@link Measured}, which also records its currency.
         *
         * @param name metric name below the module prefix, lowercase and dotted
         * @param description what one value means
         * @param unit base unit of the value; not {@link MetricUnit#MONEY}
         * @param value reads the value from the event
         * @return the declaration, to add tags
         */
        MetricDeclaration<E> measure(
                String name, String description, MetricUnit unit, ToDoubleFunction<? super E> value);
    }

    /**
     * One declared metric; tags are typed so only bounded values (enums, booleans) can become dimensions.
     *
     * @param <E> the event type
     */
    interface MetricDeclaration<E> {

        /**
         * Adds a dimension from an enum value, written in lowercase.
         *
         * @param key tag key, lowercase snake_case
         * @param value reads the enum from the event
         * @return this declaration
         */
        MetricDeclaration<E> tag(String key, Function<? super E, ? extends Enum<?>> value);

        /**
         * Adds a {@code true}/{@code false} dimension.
         *
         * @param key tag key, lowercase snake_case
         * @param value reads the flag from the event
         * @return this declaration
         */
        MetricDeclaration<E> flag(String key, Predicate<? super E> value);
    }
}
