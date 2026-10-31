using Frappe.Geo.Contracts.ValueObjects;

namespace Frappe.Geo.Contracts;

/// <summary>A country of the catalogue with its name in the asked locale.</summary>
/// <param name="Code">The ISO 3166-1 alpha-2 code.</param>
/// <param name="Name">The name in the asked locale, or the nearest one along its fallback chain.</param>
/// <param name="DefaultCurrency">The current tender, or <see langword="null"/> for a country without one.</param>
/// <param name="Status">Whether the country is in use or retired.</param>
public sealed record GeoCountry(
    CountryCode Code,
    string Name,
    CurrencyCode? DefaultCurrency,
    ReferenceStatus Status
);
