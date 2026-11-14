namespace Frappe.Geo.Application.Territory;

/// <summary>An administrative area below the first level: its level (2, 3, ...), the source's category and whether it is a local government.</summary>
public sealed record AdministrativeAreaImport(string Id, string SubdivisionCode, int Level, string Category, string Name, bool IsLocalGovernment, Coordinates? Centroid);
