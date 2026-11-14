namespace Frappe.Geo.Application.Territory;

/// <summary>A populated place, with the areas it belongs to when the source knows them (ids of <see cref="AdministrativeAreaImport"/>).</summary>
public sealed record LocalityImport(string Id, string SubdivisionCode, string? AreaId, string? LocalGovernmentId, string Name, string Category, Coordinates? Centroid);
