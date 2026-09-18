package com.frappe.platform.infrastructure.metrics;

import com.frappe.platform.Counted;
import com.frappe.platform.Measured;
import java.util.ArrayList;
import java.util.Collection;
import java.util.List;
import org.springframework.context.annotation.ClassPathScanningCandidateComponentProvider;
import org.springframework.core.type.filter.AnnotationTypeFilter;
import org.springframework.util.ClassUtils;

/** Finds every event record annotated with {@link Counted} or {@link Measured} below the application packages. */
final class AnnotatedEventScanner {

    private AnnotatedEventScanner() {}

    /**
     * Scans the packages and validates every declaration found.
     *
     * @param basePackages the application packages
     * @return the metrics of all annotated events
     * @throws InvalidBusinessMetricException for the first event whose declarations break the rules
     */
    static List<BusinessMetric> scan(Collection<String> basePackages) {
        var scanner = new ClassPathScanningCandidateComponentProvider(false);
        List.of(Counted.class, Counted.List.class, Measured.class, Measured.List.class)
                .forEach(annotation -> scanner.addIncludeFilter(new AnnotationTypeFilter(annotation)));
        var metrics = new ArrayList<BusinessMetric>();
        basePackages.stream()
                .flatMap(basePackage -> scanner.findCandidateComponents(basePackage).stream())
                .map(candidate -> ClassUtils.resolveClassName(
                        candidate.getBeanClassName(), AnnotatedEventScanner.class.getClassLoader()))
                .distinct()
                .forEach(eventType -> metrics.addAll(BusinessMetricDefinitions.of(eventType)));
        return metrics;
    }
}
