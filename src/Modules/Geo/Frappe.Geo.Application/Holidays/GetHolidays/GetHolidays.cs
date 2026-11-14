using Frappe.UseCases;

namespace Frappe.Geo.Application.Holidays.GetHolidays;

/// <summary>
/// The holidays of a country (<c>AR</c>) or of a subdivision (<c>AR-X</c>, the country's included) in a year, the
/// current one when <see cref="Year"/> is absent, from the calendars of <see cref="Scope"/> (<c>banking</c>), the general
/// ones when absent.
/// </summary>
public sealed record GetHolidays(string Jurisdiction, string? Year, string? Scope = null) : IQuery<HolidayCalendarView>;
