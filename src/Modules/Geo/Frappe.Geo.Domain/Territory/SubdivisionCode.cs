using Frappe.Geo.Domain.Codes;
using Frappe.Results;

namespace Frappe.Geo.Domain.Territory;

/// <summary>
/// ISO 3166-2 code of a country's first-level subdivision, upper case: the country's alpha-2 code, a hyphen and one to
/// three letters or digits (<c>AR-B</c>, <c>UY-MO</c>, <c>MX-CMX</c>). Lower case input is accepted and normalized.
/// </summary>
public sealed record SubdivisionCode
{
    public const int MaxLength = 6;

    private SubdivisionCode(string value) => Value = value;

    public string Value { get; }

    /// <summary>The alpha-2 code of the country it belongs to.</summary>
    public string CountryCode => Value[..2];

    public static Result<SubdivisionCode> Create(string? raw) =>
        AsciiCode.TryNormalize(raw, out var candidate) && IsWellFormed(candidate)
            ? new SubdivisionCode(candidate)
            : Error.InvalidValue(GeoErrorCodes.SubdivisionCodeInvalidFormat, ("value", raw));

    private static bool IsWellFormed(string candidate) =>
        candidate.Length is >= 4 and <= MaxLength
        && candidate[2] == '-'
        && candidate[..2].All(char.IsAsciiLetterUpper)
        && candidate[3..].All(char.IsAsciiLetterOrDigit);

    public override string ToString() => Value;
}
