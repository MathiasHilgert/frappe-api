package com.frappe.platform.infrastructure.metrics;

import com.frappe.platform.DomainEvent;
import com.frappe.platform.MetricUnit;
import io.micrometer.core.instrument.Counter;
import io.micrometer.core.instrument.DistributionSummary;
import io.micrometer.core.instrument.MeterRegistry;
import io.micrometer.core.instrument.Tags;
import java.util.List;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.context.event.EventListener;
import org.springframework.transaction.support.TransactionSynchronization;
import org.springframework.transaction.support.TransactionSynchronizationManager;

/**
 * Records the business metrics of every published domain event once its transaction commits; events of rolled-back
 * transactions are never counted. A metric that cannot be read is skipped with a warning: telemetry never fails the
 * business operation that published the event.
 */
class BusinessMetricsRecorder {

    private static final Logger log = LoggerFactory.getLogger(BusinessMetricsRecorder.class);

    private final BusinessMetricCatalog catalog;
    private final MeterRegistry registry;

    /**
     * Creates the recorder.
     *
     * @param catalog the declared metrics
     * @param registry where metrics are recorded
     */
    BusinessMetricsRecorder(BusinessMetricCatalog catalog, MeterRegistry registry) {
        this.catalog = catalog;
        this.registry = registry;
    }

    /**
     * Records the metrics of a published event, after commit when a transaction is active.
     *
     * @param event the published event
     */
    @EventListener
    void on(DomainEvent event) {
        // A plain listener plus a transaction synchronization, not @TransactionalEventListener: Spring Modulith would
        // store an outbox publication for every event and this listener.
        var metrics = catalog.metricsOf(event.getClass());
        if (metrics.isEmpty()) {
            return;
        }
        if (!TransactionSynchronizationManager.isSynchronizationActive()) {
            record(event, metrics);
            return;
        }
        TransactionSynchronizationManager.registerSynchronization(new TransactionSynchronization() {
            @Override
            public void afterCommit() {
                record(event, metrics);
            }
        });
    }

    private void record(DomainEvent event, List<BusinessMetric> metrics) {
        for (var metric : metrics) {
            try {
                record(event, metric);
            } catch (MetricRecordingException e) {
                log.atWarn()
                        .addKeyValue(LogFields.METRIC, metric.name())
                        .addKeyValue(LogFields.EVENT_TYPE, event.getClass().getName())
                        .addKeyValue(LogFields.EVENT_ID, event.eventId())
                        .setCause(e)
                        .log("Skipped business metric {}: {}", metric.name(), e.getMessage());
            }
        }
    }

    private void record(DomainEvent event, BusinessMetric metric) {
        var tags = tags(event, metric);
        switch (metric.kind()) {
            case COUNTER ->
                Counter.builder(metric.name())
                        .description(metric.description())
                        .tags(tags)
                        .register(registry)
                        .increment();
            case DISTRIBUTION -> {
                var measurement = metric.value().read(event);
                if (measurement.currency() != null) {
                    tags = tags.and(MetricRules.CURRENCY_TAG, measurement.currency());
                }
                DistributionSummary.builder(metric.name())
                        .description(metric.description())
                        .baseUnit(metric.unit().baseUnit())
                        .tags(tags)
                        // Durations get histogram buckets for latency-style SLOs; override per metric with
                        // management.metrics.distribution.slo.<name>.
                        .publishPercentileHistogram(metric.unit() == MetricUnit.SECONDS)
                        .register(registry)
                        .record(measurement.amount());
            }
        }
    }

    private static Tags tags(DomainEvent event, BusinessMetric metric) {
        var tags = Tags.empty();
        for (var tag : metric.tags()) {
            tags = tags.and(tag.key(), tag.value().apply(event));
        }
        return tags;
    }
}
