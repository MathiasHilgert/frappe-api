using Frappe.Geo.Domain.Holidays;

namespace Frappe.Geo.Domain.BusinessDays;

/// <summary>
/// What one policy treats as non-working in a country: the kinds of the general calendar's observances that close the
/// day, and the sector calendars (<see cref="CalendarScope"/>) whose every observance closes it too.
/// </summary>
public sealed record PolicyRule(IReadOnlySet<ObservanceKind> NonWorkingKinds, IReadOnlyList<CalendarScope> SectorScopes);
