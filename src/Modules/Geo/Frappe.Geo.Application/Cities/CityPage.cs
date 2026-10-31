namespace Frappe.Geo.Application.Cities;

/// <summary>One page of the city list in name order.</summary>
/// <param name="Items">The cities of the page.</param>
/// <param name="Next">The keyset position of the last city when another page follows, else <see langword="null"/>.</param>
public sealed record CityPage(IReadOnlyList<CityDto> Items, CityKey? Next);
