using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Geo.Application.Holidays.RemoveObservanceOverride;

/// <summary>
/// Drops the manual override (added or suppressed) of that date (<c>yyyy-MM-dd</c>) and name in a country's or
/// subdivision's calendar (the general one, or the sector's one <c>Scope</c> names), so the source's observance, if any,
/// counts again.
/// </summary>
public sealed record RemoveObservanceOverride(string Jurisdiction, string? Date, string? Name, string? Scope = null) : ICommand<Unit>;
