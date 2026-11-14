using Frappe.UseCases;

namespace Frappe.Geo.Application.Territory.ListSubdivisions;

/// <summary>A country's first-level subdivisions; a known country Geo holds no territory of answers "not covered", never an empty list.</summary>
public sealed record ListSubdivisions(string CountryCode) : IQuery<IReadOnlyList<SubdivisionView>>;
