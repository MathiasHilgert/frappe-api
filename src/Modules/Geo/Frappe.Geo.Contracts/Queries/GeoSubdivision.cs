namespace Frappe.Geo.Contracts.Queries;

/// <summary>A first-level subdivision: its ISO 3166-2 code, its country, its name and its category as its source names it.</summary>
public sealed record GeoSubdivision(string Code, string CountryCode, string Name, string Category);
