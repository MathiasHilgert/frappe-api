namespace Frappe.Geo.Http.Territory;

/// <summary>A first-level subdivision: ISO 3166-2 code, its country, name and category as the source writes them, and other names it is known by.</summary>
public sealed record SubdivisionResponse(string Code, string CountryCode, string Name, string Category, CentroidResponse? Centroid, IReadOnlyList<string> Aliases);
