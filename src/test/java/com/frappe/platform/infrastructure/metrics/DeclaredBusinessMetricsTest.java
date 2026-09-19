package com.frappe.platform.infrastructure.metrics;

import static com.frappe.platform.infrastructure.metrics.BusinessMetricAssert.assertThatBusinessMetric;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import com.frappe.RootLevelEvent;
import com.frappe.platform.MetricUnit;
import com.frappe.platform.infrastructure.metrics.BusinessMetricDefinitionsTest.Channel;
import com.frappe.platform.infrastructure.metrics.BusinessMetricDefinitionsTest.Money;
import com.frappe.platform.infrastructure.metrics.BusinessMetricDefinitionsTest.TabClosed;
import io.micrometer.core.instrument.simple.SimpleMeterRegistry;
import java.time.Duration;
import java.time.Instant;
import java.util.Currency;
import java.util.UUID;
import org.junit.jupiter.api.Test;

/** The escape hatch: metrics declared in code, same naming rules and guardrails as the annotations. */
class DeclaredBusinessMetricsTest {

    private static final UUID ID = UUID.fromString("01923f5e-0000-7000-8000-000000000001");

    private final SimpleMeterRegistry registry = new SimpleMeterRegistry();

    @Test
    void recordsMetricsDeclaredThroughTheFluentApi() {
        // Given
        var declared = DeclaredBusinessMetrics.collect(metrics -> {
            metrics.on(TabClosed.class)
                    .count("tabs.settled", "Tabs settled, by channel and split")
                    .tag("channel", TabClosed::channel)
                    .flag("split", TabClosed::split);
            metrics.on(TabClosed.class).measure("tabs.items", "Guests served per tab", MetricUnit.ITEMS, event -> 3);
        });
        var recorder = new BusinessMetricsRecorder(new BusinessMetricCatalog(declared), registry);

        // When
        recorder.on(new TabClosed(
                ID,
                Instant.EPOCH,
                ID,
                1,
                1,
                ID,
                Channel.TAKE_AWAY,
                true,
                new Money(1, Currency.getInstance("EUR")),
                Duration.ZERO));

        // Then
        assertThatBusinessMetric(registry, "frappe.platform.tabs.settled")
                .withTag("channel", "take_away")
                .withTag("split", "true")
                .hasCount(1);
        assertThatBusinessMetric(registry, "frappe.platform.tabs.items").hasTotal(3);
    }

    @Test
    void rejectsSecondsBecauseDurationsAreDeclaredOnDurationFields() {
        assertThatThrownBy(() -> DeclaredBusinessMetrics.collect(metrics ->
                        metrics.on(TabClosed.class).measure("tabs.open", "Open time", MetricUnit.SECONDS, event -> 1)))
                .isInstanceOf(InvalidBusinessMetricException.class)
                .hasMessageContaining("@Measured on a Duration field");
    }

    @Test
    void rejectsAnEventOutsideAModulePackageWithAClearProblem() {
        assertThatThrownBy(() -> DeclaredBusinessMetrics.collect(
                        metrics -> metrics.on(RootLevelEvent.class).count("roots.seen", "Roots")))
                .isInstanceOf(InvalidBusinessMetricException.class)
                .hasMessageContaining("must live in a module package");
    }

    @Test
    void appliesTheSameGuardrailsAsTheAnnotations() {
        assertThatThrownBy(() -> DeclaredBusinessMetrics.collect(metrics ->
                        metrics.on(TabClosed.class).count("Tabs.Settled", "").tag("tenant_id", TabClosed::channel)))
                .isInstanceOf(InvalidBusinessMetricException.class)
                .hasMessageContaining("'Tabs.Settled'")
                .hasMessageContaining("description")
                .hasMessageContaining("tag 'tenant_id'");
    }
}
