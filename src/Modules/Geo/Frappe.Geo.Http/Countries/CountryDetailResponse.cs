namespace Frappe.Geo.Http.Countries;

/// <summary>One country, as <see cref="CountryResponse"/>, with what Geo covers for it (<see cref="CapabilitiesResponse"/>).</summary>
public sealed record CountryDetailResponse(string Code, string Alpha3, string Numeric, string Name, IReadOnlyList<string> Currencies, CapabilitiesResponse Capabilities);
