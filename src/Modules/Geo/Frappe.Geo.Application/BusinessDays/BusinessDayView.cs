using Frappe.Geo.Domain.BusinessDays;
using Frappe.Geo.Domain.Holidays;

namespace Frappe.Geo.Application.BusinessDays;

/// <summary>
/// A date in a jurisdiction under a policy; <see cref="Covers"/> names the general calendars it was read from, and
/// <see cref="Attributions"/> credits the sources of every calendar read, as their terms ask.
/// </summary>
public sealed record BusinessDayView(string Jurisdiction, BusinessDayPolicy Policy, BusinessDay Day, IReadOnlyList<string> Covers, IReadOnlyList<Attribution> Attributions);
