using Frappe.Geo.Contracts.ValueObjects;

namespace Frappe.Geo.Application.Countries;

/// <summary>An ISO 3166-1 country with its address format.</summary>
/// <param name="CountryCode">The ISO 3166-1 alpha-2 code, such as AR.</param>
/// <param name="Alpha3">The ISO 3166-1 alpha-3 code, such as ARG.</param>
/// <param name="NumericCode">The ISO 3166-1 three-digit numeric code, such as 032.</param>
/// <param name="Name">The name in the requested locale, or the nearest one along its fallback chain.</param>
/// <param name="DefaultCurrencyCode">The ISO 4217 code of the current tender, or null for a country without one.</param>
/// <param name="CallingCode">The international calling code in digits without a plus sign, such as 54, or null when the country has none.</param>
/// <param name="AddressFormat">The rules for capturing an address in the country, or null when none is held, so any postal code is accepted.</param>
public sealed record CountryDetailDto(
    CountryCode CountryCode,
    string Alpha3,
    string NumericCode,
    string Name,
    CurrencyCode? DefaultCurrencyCode,
    string? CallingCode,
    AddressFormatDto? AddressFormat
);
