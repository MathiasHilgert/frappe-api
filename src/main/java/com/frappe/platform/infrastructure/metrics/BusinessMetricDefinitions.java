package com.frappe.platform.infrastructure.metrics;

import com.frappe.platform.Counted;
import com.frappe.platform.DomainEvent;
import com.frappe.platform.Measured;
import com.frappe.platform.MetricTag;
import com.frappe.platform.MetricUnit;
import java.lang.reflect.RecordComponent;
import java.util.ArrayList;
import java.util.List;

/** Turns the {@link Counted} and {@link Measured} annotations of one event record into validated metrics. */
final class BusinessMetricDefinitions {

    private BusinessMetricDefinitions() {}

    /**
     * Reads and validates the metric annotations of an event type.
     *
     * @param eventType the annotated class
     * @return its metrics, counters first, in declaration order; empty if it has none
     * @throws InvalidBusinessMetricException listing every broken rule
     */
    static List<BusinessMetric> of(Class<?> eventType) {
        var rules = new MetricRules(eventType);
        var counted = eventType.getAnnotationsByType(Counted.class);
        var measured = eventType.getAnnotationsByType(Measured.class);
        if (counted.length + measured.length == 0) {
            return List.of();
        }
        if (!eventType.isRecord() || !DomainEvent.class.isAssignableFrom(eventType)) {
            rules.reject("business metrics are declared on domain event records implementing DomainEvent");
            rules.verify();
        }
        var metrics = new ArrayList<BusinessMetric>();
        for (var counter : counted) {
            metrics.add(new BusinessMetric(
                    eventType,
                    BusinessMetric.Kind.COUNTER,
                    rules.fullName(counter.name(), counter.description()),
                    counter.description(),
                    null,
                    tags(eventType, counter.tags(), rules),
                    null));
        }
        for (var distribution : measured) {
            metrics.add(new BusinessMetric(
                    eventType,
                    BusinessMetric.Kind.DISTRIBUTION,
                    rules.fullName(distribution.name(), distribution.description()),
                    distribution.description(),
                    distribution.unit(),
                    tags(eventType, distribution.tags(), rules),
                    value(eventType, distribution, rules)));
        }
        rules.verify();
        return List.copyOf(metrics);
    }

    private static List<BusinessMetric.TagSource> tags(Class<?> eventType, MetricTag[] tags, MetricRules rules) {
        var sources = new ArrayList<BusinessMetric.TagSource>();
        for (var tag : tags) {
            rules.tagKey(tag.key());
            EventFields.find(eventType, tag.from())
                    .ifPresentOrElse(
                            component -> {
                                if (EventFields.isBounded(component.getType())) {
                                    sources.add(new BusinessMetric.TagSource(
                                            tag.key(), event -> EventFields.tagValue(event, component)));
                                } else {
                                    rules.reject("tag '" + tag.key() + "' reads '" + tag.from() + "' of type "
                                            + component.getType().getSimpleName()
                                            + ": tags must be enum or boolean fields (low cardinality); put"
                                            + " identifiers on spans as span attributes instead");
                                }
                            },
                            () -> rules.reject("tag '" + tag.key() + "' reads no field '" + tag.from() + "'"));
        }
        return List.copyOf(sources);
    }

    private static BusinessMetric.ValueSource value(Class<?> eventType, Measured measured, MetricRules rules) {
        var component = EventFields.find(eventType, measured.value());
        if (component.isEmpty()) {
            rules.reject("value reads no field '" + measured.value() + "'");
            return null;
        }
        checkUnit(component.get(), measured.unit(), rules);
        return event -> EventFields.measurement(event, component.get());
    }

    private static void checkUnit(RecordComponent component, MetricUnit unit, MetricRules rules) {
        var type = component.getType();
        var field = "'" + component.getName() + "'";
        if (EventFields.isMoney(type) && unit != MetricUnit.MONEY) {
            rules.reject("value " + field + " is money: use unit MONEY");
        } else if (EventFields.isDuration(type) && unit != MetricUnit.SECONDS) {
            rules.reject("value " + field + " is a Duration: use unit SECONDS");
        } else if (EventFields.isNumber(type) && (unit == MetricUnit.MONEY || unit == MetricUnit.SECONDS)) {
            rules.reject("value " + field + " of type " + type.getSimpleName() + " cannot be recorded as " + unit
                    + ": use a Money or Duration field");
        } else if (!EventFields.isMoney(type) && !EventFields.isDuration(type) && !EventFields.isNumber(type)) {
            rules.reject("value " + field + " of type " + type.getSimpleName() + " is not a number, Duration or Money");
        }
    }
}
