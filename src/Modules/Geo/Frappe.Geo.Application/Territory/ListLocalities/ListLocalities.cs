using Frappe.UseCases;

namespace Frappe.Geo.Application.Territory.ListLocalities;

/// <summary>
/// A page of a subdivision's localities, by name or, with <paramref name="Search"/>, most similar first. Limit and
/// cursor arrive as the client wrote them.
/// </summary>
public sealed record ListLocalities(string SubdivisionCode, string? Search, string? Limit, string? Cursor) : IQuery<LocalityPage>;
