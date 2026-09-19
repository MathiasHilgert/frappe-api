package com.frappe.platform.infrastructure.metrics;

import com.frappe.platform.DomainEvent;
import com.frappe.platform.MetricUnit;
import io.micrometer.core.instrument.Counter;
import io.micrometer.core.instrument.DistributionSummary;
import io.micrometer.core.instrument.Meter;
import io.micrometer.core.instrument.MeterRegistry;
import io.micrometer.core.instrument.Tags;
import java.util.List;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.context.event.EventListener;
import org.springframework.transaction.support.TransactionSynchronization;
import org.springframework.transaction.support.TransactionSynchronizationManager;

/**
 * Records the business metrics of every published domain event once its transaction commits; events of rolled-back
 * transactions are never counted. This is a telemetry isolation boundary: whatever goes wrong while recording one
 * metric (a throwing declaration, a missing value, a registry error) is logged once as a WARN and that metric is
 * skipped; the business caller never sees it, before or after commit.
 */
class BusinessMetricsRecorder {

    private static final Logger log = LoggerFactory.getLogger(BusinessMetricsRecorder.class);

    private final BusinessMetricCatalog catalog;
    private final MeterRegistry registry;
    // One meter per metric and tag combination, built once; tag values are bounded by the metric rules.
    private final Map<MeterKey, Meter> meters = new ConcurrentHashMap<>();

    /**
     * Creates the recorder and checks every metric name against the meters already in the registry.
     *
     * @param catalog the declared metrics
     * @param registry where metrics are recorded
     * @throws InvalidBusinessMetricException if a metric name is already used by a meter of another type
     */
    BusinessMetricsRecorder(BusinessMetricCatalog catalog, MeterRegistry registry) {
        this.catalog = catalog;
        this.registry = registry;
        catalog.all().forEach(this::rejectConflictingMeter);
    }

    /**
     * Records the metrics of a published event, after commit when a transaction is active.
     *
     * @param event the published event
     */
    @EventListener
    void on(DomainEvent event) {
        var metrics = catalog.metricsOf(event.getClass());
        if (metrics.isEmpty()) {
            return;
        }
        if (!TransactionSynchronizationManager.isSynchronizationActive()) {
            record(event, metrics);
            return;
        }
        // A plain listener plus a transaction synchronization, not @TransactionalEventListener: Spring Modulith would
        // store an outbox publication for every event and this listener.
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
            } catch (RuntimeException e) {
                // Deliberately broad (writing-code errors.md, telemetry boundary): declarations are user lambdas and
                // Micrometer may reject a meter; neither may fail or slow the business operation.
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
            case COUNTER -> ((Counter) meter(metric, tags)).increment();
            case DISTRIBUTION -> {
                var measurement = metric.value().read(event);
                if (!Double.isFinite(measurement.amount()) || measurement.amount() < 0) {
                    throw new MetricRecordingException("Value " + measurement.amount() + " of "
                            + event.getClass().getSimpleName() + " is negative or not finite");
                }
                if (measurement.currency() != null) {
                    tags = tags.and(MetricRules.CURRENCY_TAG, measurement.currency());
                }
                ((DistributionSummary) meter(metric, tags)).record(measurement.amount());
            }
        }
    }

    private Meter meter(BusinessMetric metric, Tags tags) {
        return meters.computeIfAbsent(new MeterKey(metric.name(), tags), key -> register(metric, tags));
    }

    private Meter register(BusinessMetric metric, Tags tags) {
        return switch (metric.kind()) {
            case COUNTER ->
                Counter.builder(metric.name())
                        .description(metric.description())
                        .tags(tags)
                        .register(registry);
            case DISTRIBUTION ->
                DistributionSummary.builder(metric.name())
                        .description(metric.description())
                        .baseUnit(metric.unit().baseUnit())
                        .tags(tags)
                        // Durations get histogram buckets for latency-style SLOs; override per metric with
                        // management.metrics.distribution.slo.<name>.
                        .publishPercentileHistogram(metric.unit() == MetricUnit.SECONDS)
                        .register(registry);
        };
    }

    private void rejectConflictingMeter(BusinessMetric metric) {
        var expected = metric.kind() == BusinessMetric.Kind.COUNTER ? Counter.class : DistributionSummary.class;
        var conflicting = registry.find(metric.name()).meters().stream()
                .filter(meter -> !expected.isInstance(meter))
                .findFirst();
        if (conflicting.isPresent()) {
            throw new InvalidBusinessMetricException("Business metric " + metric.name() + " is already registered as "
                    + conflicting.get().getId().getType() + "; rename the metric");
        }
    }

    private static Tags tags(DomainEvent event, BusinessMetric metric) {
        var tags = Tags.empty();
        for (var tag : metric.tags()) {
            tags = tags.and(tag.key(), tag.value().apply(event));
        }
        return tags;
    }

    /** Identity of one registered series. */
    private record MeterKey(String name, Tags tags) {}
}
