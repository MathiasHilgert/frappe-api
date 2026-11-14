using Frappe.UseCases;

namespace Frappe.Geo.Application.BusinessDays.ListBusinessDays;

/// <summary>
/// The next <see cref="Count"/> (1 to <c>IGeoQueries.MaxBusinessDays</c>) business days in a country or
/// subdivision under a policy, from <see cref="From"/> (<c>YYYY-MM-DD</c>, included).
/// </summary>
public sealed record ListBusinessDays(string Jurisdiction, string? From, string? Count, string? Policy) : IQuery<BusinessDaysView>;
