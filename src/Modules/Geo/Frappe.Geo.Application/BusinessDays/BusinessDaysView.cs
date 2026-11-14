using Frappe.Geo.Domain.BusinessDays;
using Frappe.Geo.Domain.Holidays;
using NodaTime;

namespace Frappe.Geo.Application.BusinessDays;

/// <summary>
/// The next business days in a jurisdiction under a policy, from a date (included), in order; <see cref="Covers"/> names
/// the general calendars read, and <see cref="Attributions"/> credits the sources of every calendar read, as their terms ask.
/// </summary>
public sealed record BusinessDaysView(
    string Jurisdiction,
    BusinessDayPolicy Policy,
    LocalDate From,
    IReadOnlyList<LocalDate> Dates,
    IReadOnlyList<string> Covers,
    IReadOnlyList<Attribution> Attributions);
