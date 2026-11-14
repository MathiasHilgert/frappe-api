namespace Frappe.Geo.Application.Territory;

/// <summary>An administrative area as served; <see cref="Id"/> is the public id (<c>AR:30015</c>).</summary>
public sealed record AdministrativeAreaView(string Id, string SubdivisionCode, int Level, string Category, string Name, bool IsLocalGovernment, Coordinates? Centroid);
