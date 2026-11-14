using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Geo.Application.Holidays.SuppressObservance;

/// <summary>
/// Removes the source's observance of that date (<c>yyyy-MM-dd</c>) and name from the effective calendar of a country or
/// subdivision (its general calendar, or the sector's one <c>Scope</c> names), whatever the source says later.
/// </summary>
public sealed record SuppressObservance(string Jurisdiction, string? Date, string? Name, string? Scope = null) : ICommand<Unit>;
