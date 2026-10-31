namespace Frappe.Geo.Application.Cities;

/// <summary>The keyset position of a city in name order.</summary>
/// <param name="SearchName">The folded name the list is ordered by.</param>
/// <param name="IndecId">The INDEC id that breaks ties between equal names.</param>
public sealed record CityKey(string SearchName, string IndecId);
