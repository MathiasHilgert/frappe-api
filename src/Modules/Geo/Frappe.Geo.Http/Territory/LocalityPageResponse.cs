namespace Frappe.Geo.Http.Territory;

/// <summary>One page of localities; <see cref="NextCursor"/> asks for the next one and is absent on the last page.</summary>
public sealed record LocalityPageResponse(IReadOnlyList<LocalityResponse> Localities, string? NextCursor);
