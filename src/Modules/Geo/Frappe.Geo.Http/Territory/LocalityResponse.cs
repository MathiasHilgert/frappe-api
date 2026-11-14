namespace Frappe.Geo.Http.Territory;

/// <summary>A populated place, with the ids of its level-2 area and of its local government when the source knows them.</summary>
public sealed record LocalityResponse(string Id, string SubdivisionCode, string Name, string Category, string? AreaId, string? LocalGovernmentId, CentroidResponse? Centroid);
