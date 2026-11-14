namespace Frappe.Geo.Application.Territory;

/// <summary>A locality as served, with its areas by public id when known.</summary>
public sealed record LocalityView(string Id, string SubdivisionCode, string Name, string Category, string? AreaId, string? LocalGovernmentId, Coordinates? Centroid);
