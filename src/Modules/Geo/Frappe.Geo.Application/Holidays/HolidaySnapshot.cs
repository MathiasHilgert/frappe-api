using Frappe.Geo.Domain.Holidays;

namespace Frappe.Geo.Application.Holidays;

/// <summary>
/// The calendars one holiday source publishes, each replacing the imported layer of its jurisdiction and year, and the
/// <see cref="Attribution"/> its terms ask for wherever those calendars are shown.
/// </summary>
public sealed record HolidaySnapshot(Attribution Attribution, IReadOnlyList<CalendarImport> Calendars);
