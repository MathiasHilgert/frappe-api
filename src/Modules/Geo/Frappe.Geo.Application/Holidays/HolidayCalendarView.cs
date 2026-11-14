using Frappe.Geo.Domain.Holidays;

namespace Frappe.Geo.Application.Holidays;

/// <summary>
/// The effective observances that apply in a jurisdiction in a year. <see cref="Covers"/> names the calendars they come
/// from (the country's, then the subdivision's own when Geo has it), so a reader knows what the answer includes;
/// <see cref="Attributions"/> credits the sources of those calendars, once each, as their terms ask. <see cref="Scope"/>
/// is the calendars' scope (<c>general</c>, <c>banking</c>): a sector's lists its source's facts as published.
/// </summary>
public sealed record HolidayCalendarView(
    string Jurisdiction,
    int Year,
    IReadOnlyList<string> Covers,
    IReadOnlyList<Attribution> Attributions,
    IReadOnlyList<Observance> Observances,
    string Scope);
