namespace Frappe.Geo.Application.Territory;

/// <summary>A first-level subdivision: ISO 3166-2 code, source id, name and category as the source writes them, and aliases.</summary>
public sealed record SubdivisionImport(string Code, string SourceId, string Name, string Category, Coordinates? Centroid, IReadOnlyList<string> Aliases);
