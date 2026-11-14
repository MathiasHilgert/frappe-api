namespace Frappe.Geo.Application.Territory;

/// <summary>Another name of a subdivision and where it comes from: the source's data, or added by hand.</summary>
public sealed record SubdivisionAliasView(string Alias, bool Manual);
