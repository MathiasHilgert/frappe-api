package com.frappe.platform.infrastructure.metrics;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import com.frappe.platform.Counted;
import com.frappe.platform.DomainEvent;
import com.frappe.platform.Measured;
import com.frappe.platform.MetricTag;
import com.frappe.platform.MetricUnit;
import fixtures.invalidmetrics.InvalidMetricEvents;
import java.time.Duration;
import java.time.Instant;
import java.util.Currency;
import java.util.UUID;
import org.junit.jupiter.api.Test;

/** The guardrails: every declaration problem fails with a message naming the event, the metric and the fix. */
class BusinessMetricDefinitionsTest {

    enum Channel {
        DINE_IN,
        TAKE_AWAY,
        DELIVERY
    }

    record Money(long minorUnits, Currency currency) {}

    @Counted(
            name = "tabs.closed",
            description = "Tabs closed",
            tags = {@MetricTag(key = "channel", from = "channel"), @MetricTag(key = "split", from = "split")})
    @Measured(name = "tabs.revenue", description = "Revenue of closed tabs", value = "total", unit = MetricUnit.MONEY)
    @Measured(name = "tabs.duration", description = "Time a tab stayed open", value = "open", unit = MetricUnit.SECONDS)
    record TabClosed(
            UUID eventId,
            Instant occurredAt,
            UUID aggregateId,
            long aggregateVersion,
            int eventVersion,
            UUID tenantId,
            Channel channel,
            boolean split,
            Money total,
            Duration open)
            implements DomainEvent {}

    @Test
    void acceptsAValidDeclaration() {
        BusinessMetricAssert.assertValidBusinessMetrics(TabClosed.class);
    }

    @Test
    void derivesStandardNamesUnitsAndTagsFromTheAnnotations() {
        // When
        var metrics = BusinessMetricDefinitions.of(TabClosed.class);

        // Then
        assertThat(metrics)
                .extracting(BusinessMetric::name)
                .containsExactly(
                        "frappe.platform.tabs.closed", "frappe.platform.tabs.revenue", "frappe.platform.tabs.duration");
        assertThat(metrics.get(0).tags())
                .extracting(BusinessMetric.TagSource::key)
                .containsExactly("channel", "split");
        assertThat(metrics.get(1).unit()).isEqualTo(MetricUnit.MONEY);
        assertThat(metrics.get(2).unit()).isEqualTo(MetricUnit.SECONDS);
    }

    @Test
    void rejectsANameThatIsNotLowercaseDotted() {
        assertThatThrownBy(() -> BusinessMetricDefinitions.of(InvalidMetricEvents.BadName.class))
                .isInstanceOf(InvalidBusinessMetricException.class)
                .hasMessageContaining("BadName")
                .hasMessageContaining("'Tabs-Closed'")
                .hasMessageContaining("lowercase");
    }

    @Test
    void rejectsAPrefixedOrTotalSuffixedNameAndABlankDescription() {
        assertThatThrownBy(() -> BusinessMetricDefinitions.of(InvalidMetricEvents.Redundant.class))
                .isInstanceOf(InvalidBusinessMetricException.class)
                .hasMessageContaining("'frappe.' prefix is added")
                .hasMessageContaining("'total'")
                .hasMessageContaining("description");
    }

    @Test
    void rejectsTenantAndEntityIdTags() {
        assertThatThrownBy(() -> BusinessMetricDefinitions.of(InvalidMetricEvents.HighCardinality.class))
                .isInstanceOf(InvalidBusinessMetricException.class)
                .hasMessageContaining("tag 'tenant'")
                .hasMessageContaining("tag 'tab' reads 'aggregateId' of type UUID")
                .hasMessageContaining("enum or boolean")
                .hasMessageContaining("span attribute");
    }

    @Test
    void rejectsUnknownFields() {
        assertThatThrownBy(() -> BusinessMetricDefinitions.of(InvalidMetricEvents.UnknownFields.class))
                .isInstanceOf(InvalidBusinessMetricException.class)
                .hasMessageContaining("no field 'chanel'")
                .hasMessageContaining("no field 'totl'");
    }

    @Test
    void rejectsAValueWhoseTypeDoesNotMatchTheUnit() {
        assertThatThrownBy(() -> BusinessMetricDefinitions.of(InvalidMetricEvents.WrongUnits.class))
                .isInstanceOf(InvalidBusinessMetricException.class)
                .hasMessageContaining("'total' is money: use unit MONEY")
                .hasMessageContaining("'guests' of type int cannot be recorded as MONEY");
    }
}
