package com.frappe.platform.infrastructure.metrics;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatCode;

import io.micrometer.core.instrument.Counter;
import io.micrometer.core.instrument.DistributionSummary;
import io.micrometer.core.instrument.Meter;
import io.micrometer.core.instrument.MeterRegistry;
import io.micrometer.core.instrument.Tags;
import java.util.Collection;

/**
 * Asserts on a business metric by its full name, optionally narrowed by tags, summing every matching series.
 *
 * <pre>{@code
 * assertValidBusinessMetrics(TabClosed.class);
 * assertThatBusinessMetric(registry, "frappe.order.tabs.closed").withTag("channel", "dine_in").hasCount(1);
 * }</pre>
 */
public final class BusinessMetricAssert {

    private final MeterRegistry registry;
    private final String name;
    private final Tags tags;

    private BusinessMetricAssert(MeterRegistry registry, String name, Tags tags) {
        this.registry = registry;
        this.name = name;
        this.tags = tags;
    }

    /**
     * Starts an assertion on the metric with this full name ({@code frappe.<module>.<name>}).
     *
     * @param registry the registry the application records into
     * @param name full metric name
     * @return the assertion
     */
    public static BusinessMetricAssert assertThatBusinessMetric(MeterRegistry registry, String name) {
        return new BusinessMetricAssert(registry, name, Tags.empty());
    }

    /**
     * Checks the {@code @Counted}/{@code @Measured} declarations of event types without starting Spring: the same rules
     * the application enforces at startup. Use it in a module's event tests.
     *
     * @param eventTypes the annotated event records
     */
    public static void assertValidBusinessMetrics(Class<?>... eventTypes) {
        for (var eventType : eventTypes) {
            assertThatCode(() -> BusinessMetricDefinitions.of(eventType))
                    .as("business metrics of %s", eventType.getSimpleName())
                    .doesNotThrowAnyException();
        }
    }

    /**
     * Narrows the assertion to series carrying this tag.
     *
     * @param key tag key
     * @param value expected tag value
     * @return the narrowed assertion
     */
    public BusinessMetricAssert withTag(String key, String value) {
        return new BusinessMetricAssert(registry, name, tags.and(key, value));
    }

    /**
     * Asserts how often the metric was recorded: counter increments or distribution samples.
     *
     * @param expected expected count
     * @return this assertion
     */
    public BusinessMetricAssert hasCount(long expected) {
        var counted = meters().stream()
                .mapToDouble(meter -> switch (meter) {
                    case Counter counter -> counter.count();
                    case DistributionSummary summary -> summary.count();
                    default -> 0;
                })
                .sum();
        assertThat((long) counted).as("count of %s%s", name, tags).isEqualTo(expected);
        return this;
    }

    /**
     * Asserts the sum of all recorded values of a distribution, in its base unit.
     *
     * @param expected expected total
     * @return this assertion
     */
    public BusinessMetricAssert hasTotal(double expected) {
        var total = registry.find(name).tags(tags).summaries().stream()
                .mapToDouble(DistributionSummary::totalAmount)
                .sum();
        assertThat(total).as("total of %s%s", name, tags).isEqualTo(expected);
        return this;
    }

    private Collection<Meter> meters() {
        var meters = registry.find(name).tags(tags).meters();
        assertThat(meters)
                .as(
                        "business metric %s%s; recorded: %s",
                        name,
                        tags,
                        registry.getMeters().stream()
                                .map(meter -> meter.getId().getName())
                                .filter(recorded -> recorded.startsWith("frappe."))
                                .distinct()
                                .toList())
                .isNotEmpty();
        return meters;
    }
}
