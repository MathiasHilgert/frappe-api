namespace Frappe.Geo.Http.Territory;

/// <summary>An administrative area below the first level: its level (2, 3, ...), its category as the source writes it, and whether it is a local government.</summary>
public sealed record AreaResponse(string Id, string SubdivisionCode, int Level, string Category, string Name, bool IsLocalGovernment, CentroidResponse? Centroid);
