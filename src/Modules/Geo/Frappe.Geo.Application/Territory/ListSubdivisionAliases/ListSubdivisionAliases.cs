using Frappe.UseCases;

namespace Frappe.Geo.Application.Territory.ListSubdivisionAliases;

/// <summary>A subdivision's aliases, the source's and the manual ones.</summary>
public sealed record ListSubdivisionAliases(string SubdivisionCode) : IQuery<IReadOnlyList<SubdivisionAliasView>>;
