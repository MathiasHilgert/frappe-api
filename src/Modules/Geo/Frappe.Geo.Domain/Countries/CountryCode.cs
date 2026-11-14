using Frappe.Geo.Domain.Codes;
using Frappe.Results;

namespace Frappe.Geo.Domain.Countries;

/// <summary>ISO 3166-1 alpha-2 country code, upper case (<c>AR</c>). Lower case input is accepted and normalized.</summary>
public sealed record CountryCode
{
    private CountryCode(string value) => Value = value;

    public string Value { get; }

    public static Result<CountryCode> Create(string? raw) =>
        AsciiCode.TryNormalize(raw, out var candidate) && candidate.Length == 2 && candidate.All(char.IsAsciiLetterUpper)
            ? new CountryCode(candidate)
            : Error.InvalidValue(GeoErrorCodes.CountryCodeInvalidFormat, ("value", raw));

    public override string ToString() => Value;
}
