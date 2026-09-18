package fixtures.invalidmetrics;

import com.frappe.platform.Counted;
import com.frappe.platform.DomainEvent;
import com.frappe.platform.Measured;
import com.frappe.platform.MetricTag;
import com.frappe.platform.MetricUnit;
import java.time.Instant;
import java.util.Currency;
import java.util.UUID;

/**
 * Events with invalid business metric declarations. They live outside {@code com.frappe} on purpose: the startup scan
 * covers {@code com.frappe} and would (correctly) refuse to start every test context that sees them.
 */
public final class InvalidMetricEvents {

    private InvalidMetricEvents() {}

    public record Money(long minorUnits, Currency currency) {}

    @Counted(name = "Tabs-Closed", description = "Tabs closed")
    public record BadName(UUID eventId, Instant occurredAt, UUID aggregateId, long aggregateVersion, int eventVersion)
            implements DomainEvent {}

    @Counted(name = "frappe.platform.tabs.closed.total", description = " ")
    public record Redundant(UUID eventId, Instant occurredAt, UUID aggregateId, long aggregateVersion, int eventVersion)
            implements DomainEvent {}

    @Counted(
            name = "tabs.closed",
            description = "Tabs closed",
            tags = {@MetricTag(key = "tenant", from = "tenantId"), @MetricTag(key = "tab", from = "aggregateId")})
    public record HighCardinality(
            UUID eventId, Instant occurredAt, UUID aggregateId, long aggregateVersion, int eventVersion, UUID tenantId)
            implements DomainEvent {}

    @Counted(name = "tabs.closed", description = "Tabs closed", tags = @MetricTag(key = "channel", from = "chanel"))
    @Measured(name = "tabs.revenue", description = "Revenue", value = "totl", unit = MetricUnit.MONEY)
    public record UnknownFields(
            UUID eventId, Instant occurredAt, UUID aggregateId, long aggregateVersion, int eventVersion)
            implements DomainEvent {}

    @Measured(name = "tabs.revenue", description = "Revenue", value = "total", unit = MetricUnit.ITEMS)
    @Measured(name = "tabs.guests", description = "Guests", value = "guests", unit = MetricUnit.MONEY)
    public record WrongUnits(
            UUID eventId,
            Instant occurredAt,
            UUID aggregateId,
            long aggregateVersion,
            int eventVersion,
            Money total,
            int guests)
            implements DomainEvent {}
}
