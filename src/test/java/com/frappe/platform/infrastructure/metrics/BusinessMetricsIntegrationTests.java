package com.frappe.platform.infrastructure.metrics;

import static com.frappe.platform.infrastructure.metrics.BusinessMetricAssert.assertThatBusinessMetric;
import static org.assertj.core.api.Assertions.assertThat;

import com.frappe.TestNatsConfiguration;
import com.frappe.TestcontainersConfiguration;
import com.frappe.platform.IdGenerator;
import com.frappe.platform.infrastructure.ids.TestIds;
import com.frappe.platform.infrastructure.metrics.BusinessMetricDefinitionsTest.Channel;
import com.frappe.platform.infrastructure.metrics.BusinessMetricDefinitionsTest.Money;
import com.frappe.platform.infrastructure.metrics.BusinessMetricDefinitionsTest.TabClosed;
import io.micrometer.core.instrument.MeterRegistry;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.time.ZoneOffset;
import java.util.Currency;
import java.util.regex.Pattern;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.ApplicationEventPublisher;
import org.springframework.context.annotation.Import;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.transaction.support.TransactionTemplate;

/** A domain event annotated with metrics is counted after its transaction commits, with no wiring in the module. */
@SpringBootTest
@Import({TestcontainersConfiguration.class, TestNatsConfiguration.class})
@ActiveProfiles("local")
class BusinessMetricsIntegrationTests {

    // A tenant, or any "<entity>_id" / "<entity>Id" key. A bare "id" is the JVM memory pool name (bounded).
    private static final Pattern IDENTIFYING_TAG = Pattern.compile(".*(?i:tenant).*|.*[._](?i:id)|.*[a-z]Id");

    @Autowired
    ApplicationEventPublisher events;

    @Autowired
    TransactionTemplate transactions;

    @Autowired
    MeterRegistry registry;

    @Test
    void countsAnnotatedEventsOfCommittedTransactionsOnly() {
        // When one transaction commits and another rolls back
        transactions.executeWithoutResult(status -> events.publishEvent(tabClosed(Channel.TAKE_AWAY)));
        transactions.executeWithoutResult(status -> {
            events.publishEvent(tabClosed(Channel.DINE_IN));
            status.setRollbackOnly();
        });

        // Then
        assertThatBusinessMetric(registry, "frappe.platform.tabs.closed")
                .withTag("channel", "take_away")
                .hasCount(1);
        assertThat(registry.find("frappe.platform.tabs.closed")
                        .tag("channel", "dine_in")
                        .counter())
                .isNull();
    }

    @Test
    void noMetricCarriesATenantOrEntityIdTag() {
        // Given business and infrastructure metrics were recorded
        transactions.executeWithoutResult(status -> events.publishEvent(tabClosed(Channel.DELIVERY)));

        // Then
        assertThat(registry.getMeters())
                .flatExtracting(meter -> meter.getId().getTags())
                .extracting(tag -> tag.getKey())
                .noneMatch(key -> IDENTIFYING_TAG.matcher(key).matches());
    }

    private final IdGenerator ids =
            TestIds.withClock(Clock.fixed(Instant.parse("2026-09-18T12:00:00Z"), ZoneOffset.UTC));

    private TabClosed tabClosed(Channel channel) {
        var id = ids.newId();
        return new TabClosed(
                id,
                Instant.EPOCH,
                id,
                1,
                1,
                id,
                channel,
                false,
                new Money(500, Currency.getInstance("EUR")),
                Duration.ofMinutes(10));
    }
}
