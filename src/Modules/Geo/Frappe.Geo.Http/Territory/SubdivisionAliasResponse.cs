namespace Frappe.Geo.Http.Territory;

/// <summary>An alias of a subdivision and its <c>origin</c>: <c>source</c> (the territory sync keeps it) or <c>manual</c> (added by hand).</summary>
public sealed record SubdivisionAliasResponse(string Alias, string Origin);
