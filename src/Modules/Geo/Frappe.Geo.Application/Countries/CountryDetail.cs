namespace Frappe.Geo.Application.Countries;

/// <summary>One country as served on its own: its summary and what Geo covers for it.</summary>
public sealed record CountryDetail(CountrySummary Country, CountryCapabilities Capabilities);
