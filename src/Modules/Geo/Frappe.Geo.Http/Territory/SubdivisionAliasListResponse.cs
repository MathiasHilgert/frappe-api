namespace Frappe.Geo.Http.Territory;

/// <summary>A subdivision's aliases, the source's and the manual ones, by alias.</summary>
public sealed record SubdivisionAliasListResponse(IReadOnlyList<SubdivisionAliasResponse> Aliases);
