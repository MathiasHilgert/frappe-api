package com.frappe.platform.infrastructure.metrics;

import com.frappe.platform.BusinessMetricsDeclaration;
import io.micrometer.core.instrument.MeterRegistry;
import java.util.ArrayList;
import org.springframework.beans.factory.BeanFactory;
import org.springframework.beans.factory.ObjectProvider;
import org.springframework.boot.autoconfigure.AutoConfigurationPackages;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

/** Wires business metrics: the catalog validated at startup and the recorder listening to domain events. */
@Configuration(proxyBeanMethods = false)
class BusinessMetricsConfiguration {

    /** Creates the configuration; instantiated by Spring. */
    BusinessMetricsConfiguration() {}

    /**
     * Collects annotated events and {@link BusinessMetricsDeclaration} beans; an invalid declaration stops startup.
     *
     * @param beanFactory source of the application packages to scan
     * @param declarations metrics declared in code
     * @return the catalog
     */
    @Bean
    BusinessMetricCatalog businessMetricCatalog(
            BeanFactory beanFactory, ObjectProvider<BusinessMetricsDeclaration> declarations) {
        var metrics = new ArrayList<>(AnnotatedEventScanner.scan(AutoConfigurationPackages.get(beanFactory)));
        declarations
                .orderedStream()
                .forEach(declaration -> metrics.addAll(DeclaredBusinessMetrics.collect(declaration)));
        return new BusinessMetricCatalog(metrics);
    }

    /**
     * Records business metrics of published domain events.
     *
     * @param catalog the declared metrics
     * @param registry where metrics are recorded
     * @return the recorder
     */
    @Bean
    BusinessMetricsRecorder businessMetricsRecorder(BusinessMetricCatalog catalog, MeterRegistry registry) {
        return new BusinessMetricsRecorder(catalog, registry);
    }
}
