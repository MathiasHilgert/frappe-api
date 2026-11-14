namespace Frappe.Geo.Application.Territory;

/// <summary>A first-level subdivision as served.</summary>
public sealed record SubdivisionView(string Code, string CountryCode, string Name, string Category, Coordinates? Centroid, IReadOnlyList<string> Aliases);
