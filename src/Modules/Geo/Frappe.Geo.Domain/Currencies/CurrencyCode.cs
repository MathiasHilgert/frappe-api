using Frappe.Geo.Domain.Codes;
using Frappe.Results;

namespace Frappe.Geo.Domain.Currencies;

/// <summary>ISO 4217 alphabetic currency code, upper case (<c>ARS</c>). Lower case input is accepted and normalized.</summary>
public sealed record CurrencyCode
{
    private CurrencyCode(string value) => Value = value;

    public string Value { get; }

    public static Result<CurrencyCode> Create(string? raw) =>
        AsciiCode.TryNormalize(raw, out var candidate) && candidate.Length == 3 && candidate.All(char.IsAsciiLetterUpper)
            ? new CurrencyCode(candidate)
            : Error.InvalidValue(GeoErrorCodes.CurrencyCodeInvalidFormat, ("value", raw));

    public override string ToString() => Value;
}
