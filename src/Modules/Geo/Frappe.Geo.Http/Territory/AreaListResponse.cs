namespace Frappe.Geo.Http.Territory;

/// <summary>A subdivision's administrative areas, ordered by level then name.</summary>
public sealed record AreaListResponse(IReadOnlyList<AreaResponse> Areas);
