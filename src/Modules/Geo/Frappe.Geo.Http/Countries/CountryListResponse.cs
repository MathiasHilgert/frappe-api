namespace Frappe.Geo.Http.Countries;

/// <summary>Every country, ordered by code.</summary>
public sealed record CountryListResponse(IReadOnlyList<CountryResponse> Countries);
