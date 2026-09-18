package com.frappe.platform.infrastructure.metrics;

import java.util.Collection;
import java.util.List;
import java.util.Map;
import java.util.stream.Collectors;

/** Every business metric of the application, indexed by event type; built once at startup. */
final class BusinessMetricCatalog {

    private final Map<Class<?>, List<BusinessMetric>> byEventType;

    /**
     * Indexes the metrics.
     *
     * @param metrics all declared metrics
     * @throws InvalidBusinessMetricException if two declarations share a metric name
     */
    BusinessMetricCatalog(Collection<BusinessMetric> metrics) {
        rejectDuplicateNames(metrics);
        this.byEventType = metrics.stream().collect(Collectors.groupingBy(BusinessMetric::eventType));
    }

    /**
     * The metrics recorded for an event type.
     *
     * @param eventType the concrete event class
     * @return its metrics, empty if none
     */
    List<BusinessMetric> metricsOf(Class<?> eventType) {
        return byEventType.getOrDefault(eventType, List.of());
    }

    // A second declaration with the same name would silently merge two meanings into one series.
    private static void rejectDuplicateNames(Collection<BusinessMetric> metrics) {
        var duplicates = metrics.stream()
                .collect(Collectors.groupingBy(BusinessMetric::name, Collectors.counting()))
                .entrySet()
                .stream()
                .filter(entry -> entry.getValue() > 1)
                .map(Map.Entry::getKey)
                .sorted()
                .toList();
        if (!duplicates.isEmpty()) {
            throw new InvalidBusinessMetricException(
                    "Business metrics declared more than once: " + duplicates + "; give each metric one declaration");
        }
    }
}
