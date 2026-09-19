package com.frappe.platform.infrastructure.metrics;

import com.frappe.platform.BusinessMetrics;
import com.frappe.platform.BusinessMetricsDeclaration;
import com.frappe.platform.DomainEvent;
import com.frappe.platform.MetricUnit;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.function.Function;
import java.util.function.Predicate;
import java.util.function.ToDoubleFunction;

/** Collects the metrics a {@link BusinessMetricsDeclaration} declares in code and validates them like annotations. */
final class DeclaredBusinessMetrics implements BusinessMetrics {

    private final List<Declaration<?>> declarations = new ArrayList<>();

    private DeclaredBusinessMetrics() {}

    /**
     * Runs a declaration and returns its validated metrics.
     *
     * @param declaration the code declaring metrics
     * @return the metrics
     * @throws InvalidBusinessMetricException listing every broken rule of the first invalid event type
     */
    static List<BusinessMetric> collect(BusinessMetricsDeclaration declaration) {
        var metrics = new DeclaredBusinessMetrics();
        declaration.declare(metrics);
        return metrics.declarations.stream().map(Declaration::build).toList();
    }

    @Override
    public <E extends DomainEvent> EventMetrics<E> on(Class<E> eventType) {
        return new EventMetrics<>() {
            @Override
            public MetricDeclaration<E> count(String name, String description) {
                return add(new Declaration<>(eventType, BusinessMetric.Kind.COUNTER, name, description, null, null));
            }

            @Override
            public MetricDeclaration<E> measure(
                    String name, String description, MetricUnit unit, ToDoubleFunction<? super E> value) {
                return add(
                        new Declaration<>(eventType, BusinessMetric.Kind.DISTRIBUTION, name, description, unit, value));
            }
        };
    }

    private <E> Declaration<E> add(Declaration<E> declaration) {
        declarations.add(declaration);
        return declaration;
    }

    /** One metric being declared; validated when built. */
    private static final class Declaration<E> implements MetricDeclaration<E> {

        private final Class<E> eventType;
        private final BusinessMetric.Kind kind;
        private final String name;
        private final String description;
        private final MetricUnit unit;
        private final ToDoubleFunction<? super E> value;
        private final List<String> tagKeys = new ArrayList<>();
        private final List<BusinessMetric.TagSource> tags = new ArrayList<>();

        Declaration(
                Class<E> eventType,
                BusinessMetric.Kind kind,
                String name,
                String description,
                MetricUnit unit,
                ToDoubleFunction<? super E> value) {
            this.eventType = eventType;
            this.kind = kind;
            this.name = name;
            this.description = description;
            this.unit = unit;
            this.value = value;
        }

        @Override
        public MetricDeclaration<E> tag(String key, Function<? super E, ? extends Enum<?>> value) {
            return addTag(key, event -> {
                var constant = value.apply(eventType.cast(event));
                if (constant == null) {
                    throw new MetricRecordingException(
                            "Tag '" + key + "' of " + eventType.getSimpleName() + " is null");
                }
                return constant.name().toLowerCase(Locale.ROOT);
            });
        }

        @Override
        public MetricDeclaration<E> flag(String key, Predicate<? super E> value) {
            return addTag(key, event -> String.valueOf(value.test(eventType.cast(event))));
        }

        private MetricDeclaration<E> addTag(String key, Function<Object, String> reader) {
            tagKeys.add(key);
            tags.add(new BusinessMetric.TagSource(key, reader));
            return this;
        }

        BusinessMetric build() {
            var rules = new MetricRules(eventType);
            var fullName = rules.fullName(name, description);
            tagKeys.forEach(rules::tagKey);
            if (unit == MetricUnit.SECONDS) {
                rules.reject("metric '" + name + "' records a duration: declare it with @Measured on a Duration field,"
                        + " which converts it to seconds");
            }
            if (unit == MetricUnit.MONEY) {
                rules.reject("metric '" + name + "' records money: declare it with @Measured, which adds the currency");
            }
            rules.verify();
            return new BusinessMetric(eventType, kind, fullName, description, unit, List.copyOf(tags), measurement());
        }

        private BusinessMetric.ValueSource measurement() {
            if (value == null) {
                return null;
            }
            return event -> new BusinessMetric.Measurement(value.applyAsDouble(eventType.cast(event)), null);
        }
    }
}
