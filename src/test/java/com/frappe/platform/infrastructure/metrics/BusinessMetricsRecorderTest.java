package com.frappe.platform.infrastructure.metrics;

import static com.frappe.platform.infrastructure.metrics.BusinessMetricAssert.assertThatBusinessMetric;
import static org.assertj.core.api.Assertions.assertThat;

import com.frappe.platform.DomainEvent;
import com.frappe.platform.infrastructure.metrics.BusinessMetricDefinitionsTest.Channel;
import com.frappe.platform.infrastructure.metrics.BusinessMetricDefinitionsTest.Money;
import com.frappe.platform.infrastructure.metrics.BusinessMetricDefinitionsTest.TabClosed;
import io.micrometer.core.instrument.simple.SimpleMeterRegistry;
import java.time.Duration;
import java.time.Instant;
import java.util.Currency;
import java.util.UUID;
import org.junit.jupiter.api.Test;
import org.springframework.transaction.support.TransactionSynchronizationManager;
import org.springframework.transaction.support.TransactionSynchronizationUtils;

class BusinessMetricsRecorderTest {

    private static final UUID ID = UUID.fromString("01923f5e-0000-7000-8000-000000000001");

    record Unmeasured(UUID eventId, Instant occurredAt, UUID aggregateId, long aggregateVersion, int eventVersion)
            implements DomainEvent {}

    private final SimpleMeterRegistry registry = new SimpleMeterRegistry();
    private final BusinessMetricsRecorder recorder = new BusinessMetricsRecorder(
            new BusinessMetricCatalog(BusinessMetricDefinitions.of(TabClosed.class)), registry);

    private static TabClosed tabClosed(Channel channel, long cents, Duration open) {
        return new TabClosed(
                ID, Instant.EPOCH, ID, 1, 1, ID, channel, false, new Money(cents, Currency.getInstance("EUR")), open);
    }

    @Test
    void recordsCountersAndDistributionsWithTheirTagsOutsideATransaction() {
        // When
        recorder.on(tabClosed(Channel.DINE_IN, 1250, Duration.ofMinutes(30)));
        recorder.on(tabClosed(Channel.TAKE_AWAY, 800, Duration.ofMinutes(5)));

        // Then
        assertThatBusinessMetric(registry, "frappe.platform.tabs.closed")
                .withTag("channel", "dine_in")
                .withTag("split", "false")
                .hasCount(1);
        assertThatBusinessMetric(registry, "frappe.platform.tabs.revenue")
                .withTag("currency", "EUR")
                .hasCount(2)
                .hasTotal(2050);
        assertThatBusinessMetric(registry, "frappe.platform.tabs.duration").hasTotal(35 * 60);
    }

    @Test
    void recordsOnlyAfterTheTransactionCommits() {
        // Given a transaction is active
        TransactionSynchronizationManager.initSynchronization();
        try {
            // When
            recorder.on(tabClosed(Channel.DINE_IN, 100, Duration.ofMinutes(1)));

            // Then nothing is recorded before the commit, and the count arrives after it
            assertThat(registry.find("frappe.platform.tabs.closed").counter()).isNull();
            TransactionSynchronizationUtils.invokeAfterCommit(TransactionSynchronizationManager.getSynchronizations());
            assertThatBusinessMetric(registry, "frappe.platform.tabs.closed").hasCount(1);
        } finally {
            TransactionSynchronizationManager.clearSynchronization();
        }
    }

    @Test
    void neverFailsThePublisherWhenAValueIsMissing() {
        // When a money value is null
        recorder.on(new TabClosed(ID, Instant.EPOCH, ID, 1, 1, ID, Channel.DINE_IN, false, null, Duration.ZERO));

        // Then the other metrics are still recorded and nothing is thrown
        assertThatBusinessMetric(registry, "frappe.platform.tabs.closed").hasCount(1);
        assertThat(registry.find("frappe.platform.tabs.revenue").summary()).isNull();
    }

    @Test
    void ignoresEventsWithoutDeclaredMetrics() {
        // When
        recorder.on(new Unmeasured(ID, Instant.EPOCH, ID, 1, 1));

        // Then
        assertThat(registry.getMeters()).isEmpty();
    }
}
