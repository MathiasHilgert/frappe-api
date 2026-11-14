using Frappe.UseCases;

namespace Frappe.Geo.Application.BusinessDays.GetBusinessDay;

/// <summary>
/// What a date (<c>YYYY-MM-DD</c>) is in a country (<c>AR</c>) or subdivision (<c>AR-X</c>) under a policy
/// (<c>banking</c>, <c>labor</c>, <c>commercial</c>): a business day or not, and its kind.
/// </summary>
public sealed record GetBusinessDay(string Jurisdiction, string Date, string? Policy) : IQuery<BusinessDayView>;
