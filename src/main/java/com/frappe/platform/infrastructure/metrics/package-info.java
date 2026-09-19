/**
 * Business metrics from domain events: discovers {@code @Counted}/{@code @Measured} declarations and
 * {@code BusinessMetricsDeclaration} beans at startup, validates them, and records them in Micrometer after the
 * publishing transaction commits.
 */
package com.frappe.platform.infrastructure.metrics;
