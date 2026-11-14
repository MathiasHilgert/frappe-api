namespace Frappe.Geo.Http.Territory;

/// <summary>A country's first-level subdivisions, ordered by code.</summary>
public sealed record SubdivisionListResponse(IReadOnlyList<SubdivisionResponse> Subdivisions);
