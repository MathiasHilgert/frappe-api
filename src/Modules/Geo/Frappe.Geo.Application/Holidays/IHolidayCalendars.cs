using Frappe.Geo.Domain.Countries;
using Frappe.Geo.Domain.Holidays;

namespace Frappe.Geo.Application.Holidays;

/// <summary>The stored calendars, one per jurisdiction, year and scope.</summary>
public interface IHolidayCalendars
{
    Task<HolidayCalendar?> FindAsync(Jurisdiction jurisdiction, HolidayYear year, CalendarScope scope, CancellationToken cancellationToken);

    /// <summary>Which calendars a country holds of its own (its subdivisions' aside): each scope and year stored.</summary>
    Task<IReadOnlyList<(CalendarScope Scope, int Year)>> CoverageAsync(CountryCode country, CancellationToken cancellationToken);

    /// <summary>Tracks a new calendar; the unit of work saves it.</summary>
    void Add(HolidayCalendar calendar);
}
