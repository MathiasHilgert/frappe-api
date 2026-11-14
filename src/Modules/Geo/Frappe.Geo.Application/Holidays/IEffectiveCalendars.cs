using Frappe.Geo.Domain.Holidays;

namespace Frappe.Geo.Application.Holidays;

/// <summary>
/// Outbound port: the effective calendar of one jurisdiction, year and scope, as the business-day reads use it (the
/// adapter may cache it). <see langword="null"/> means Geo has no such calendar, which is not an empty one.
/// </summary>
public interface IEffectiveCalendars
{
    Task<EffectiveCalendar?> FindAsync(Jurisdiction jurisdiction, HolidayYear year, CalendarScope scope, CancellationToken cancellationToken);
}
