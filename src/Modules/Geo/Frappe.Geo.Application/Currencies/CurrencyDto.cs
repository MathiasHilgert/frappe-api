using Frappe.Geo.Contracts.ValueObjects;

namespace Frappe.Geo.Application.Currencies;

/// <summary>An ISO 4217 currency in use.</summary>
/// <param name="CurrencyCode">The ISO 4217 alphabetic code, such as ARS.</param>
/// <param name="NumericCode">The ISO 4217 three-digit numeric code, such as 032, or null when ISO assigns none.</param>
/// <param name="MinorUnit">The decimal digits of an amount, 0 to 4, or null for a currency without a minor unit.</param>
/// <param name="Name">The name in the requested locale, or the nearest one along its fallback chain.</param>
public sealed record CurrencyDto(
    CurrencyCode CurrencyCode,
    string? NumericCode,
    MinorUnit? MinorUnit,
    string Name
);
