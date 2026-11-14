using Frappe.Aggregates;

namespace Frappe.Geo.Domain.Holidays;

/// <summary>The effective observances of a jurisdiction's year changed: what anything depending on the calendar must recompute.</summary>
public sealed record HolidayCalendarChanged(Jurisdiction Jurisdiction, HolidayYear Year) : IDomainEvent
{
    /// <summary>Which of the jurisdiction's calendars of that year changed; the general one unless said otherwise.</summary>
    public CalendarScope Scope { get; init; } = CalendarScope.General;
}
