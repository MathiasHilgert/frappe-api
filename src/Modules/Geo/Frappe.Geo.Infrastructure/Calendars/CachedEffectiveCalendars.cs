using Frappe.Geo.Application.Holidays;
using Frappe.Geo.Domain.Holidays;
using ZiggyCreatures.Caching.Fusion;

namespace Frappe.Geo.Infrastructure.Calendars;

/// <summary>
/// The effective calendars through the platform cache, one entry per stored calendar (jurisdiction, scope, year): a
/// subdivision's answer composes its country's entry and its own, so a change of either evicts only that entry
/// (<see cref="CalendarCacheInvalidation"/>). No projection is persisted: a miss computes the calendar from its layers.
/// </summary>
internal sealed class CachedEffectiveCalendars(IHolidayCalendars calendars, IFusionCache cache) : IEffectiveCalendars
{
    public async Task<EffectiveCalendar?> FindAsync(Jurisdiction jurisdiction, HolidayYear year, CalendarScope scope, CancellationToken cancellationToken)
    {
        var cached = await cache.GetOrSetAsync<CachedCalendar>(
            CalendarCacheKeys.Key(jurisdiction.Value, scope.Value, year.Value),
            async (_, token) => await calendars.FindAsync(jurisdiction, year, scope, token) is { } calendar
                ? CachedCalendar.Of(EffectiveCalendar.Of(calendar))
                : CachedCalendar.Missing(jurisdiction),
            tags: CalendarCacheKeys.Tags(jurisdiction, scope, year),
            token: cancellationToken);
        return cached.ToCalendar();
    }
}
