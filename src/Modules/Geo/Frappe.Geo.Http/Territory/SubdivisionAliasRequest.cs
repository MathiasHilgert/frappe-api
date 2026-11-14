namespace Frappe.Geo.Http.Territory;

/// <summary>Another name a subdivision is known by, up to 200 characters.</summary>
public sealed record SubdivisionAliasRequest(string? Alias);
