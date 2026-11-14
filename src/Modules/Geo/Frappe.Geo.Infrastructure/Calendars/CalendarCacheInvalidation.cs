using Frappe.Caching;
using Frappe.Geo.Contracts.Holidays;
using Frappe.IntegrationEvents;

namespace Frappe.Geo.Infrastructure.Calendars;

/// <summary>
/// Evicts the cached calendar a <see cref="HolidaysChanged"/> names once its change is committed: the one of its
/// scope, or every scope of that jurisdiction and year when the event predates the scope field.
/// </summary>
internal sealed class CalendarCacheInvalidation(CacheInvalidator invalidator) : IAfterCommitHandler<HolidaysChanged>
{
    public Task HandleAsync(HolidaysChanged integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var tag = integrationEvent.Scope is { } scope
            ? CalendarCacheKeys.ScopedTag(integrationEvent.Jurisdiction, scope, integrationEvent.Year)
            : CalendarCacheKeys.Tag(integrationEvent.Jurisdiction, integrationEvent.Year);
        return invalidator.InvalidateAsync(tag, cancellationToken);
    }
}
