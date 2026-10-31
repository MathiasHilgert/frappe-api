namespace Frappe.Geo.Application.Cities;

/// <summary>One word of a city search.</summary>
/// <param name="Plain">The folded word.</param>
/// <param name="Escaped">The folded word escaped for a <c>LIKE</c> pattern.</param>
public sealed record CitySearchToken(string Plain, string Escaped);
