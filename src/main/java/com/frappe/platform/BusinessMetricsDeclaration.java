package com.frappe.platform;

/**
 * A bean that declares business metrics in code through {@link BusinessMetrics}; the platform collects every such bean
 * at startup and validates it like the annotations.
 */
@FunctionalInterface
public interface BusinessMetricsDeclaration {

    /**
     * Declares the metrics.
     *
     * @param metrics the declaration API
     */
    void declare(BusinessMetrics metrics);
}
