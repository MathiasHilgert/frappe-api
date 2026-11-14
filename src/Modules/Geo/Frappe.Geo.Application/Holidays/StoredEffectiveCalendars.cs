using Frappe.Geo.Domain.Holidays;

namespace Frappe.Geo.Application.Holidays;

/// <summary>The effective calendars read straight from storage, for the reads that must see a change at once.</summary>
internal sealed class StoredEffectiveCalendars(IHolidayCalendars calendars) : IEffectiveCalendars
{
    public async Task<EffectiveCalendar?> FindAsync(Jurisdiction jurisdiction, HolidayYear year, CalendarScope scope, CancellationToken cancellationToken) =>
        await calendars.FindAsync(jurisdiction, year, scope, cancellationToken) is { } calendar ? EffectiveCalendar.Of(calendar) : null;
}
