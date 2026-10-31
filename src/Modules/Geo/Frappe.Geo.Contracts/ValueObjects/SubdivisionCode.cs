using System.Buffers;
using System.Text;

namespace Frappe.Geo.Contracts.ValueObjects;

/// <summary>An ISO 3166-2 subdivision code in upper case, such as <c>AR-X</c>.</summary>
/// <remarks>Checks the format only; whether the subdivision exists is the geo catalogue's decision.</remarks>
public sealed record SubdivisionCode
{
    private const int CountryLength = 2;
    private const int MaxSuffixLength = 3;

    private static readonly SearchValues<char> _alphabet = SearchValues.Create(
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"
    );

    private SubdivisionCode(string value)
    {
        Value = value;
        Country = CountryCode.Create(value[..CountryLength]).Value;
    }

    /// <summary>Gets the error returned for a value that is not a country code, a hyphen and one to three letters or digits.</summary>
    public static Error Invalid { get; } = Error.Validation("geo.subdivision_code.invalid");

    /// <summary>Gets the upper-case code.</summary>
    public string Value { get; }

    /// <summary>Gets the country the subdivision belongs to.</summary>
    public CountryCode Country { get; }

    /// <summary>Builds a subdivision code from untrusted input, trimmed and upper-cased first.</summary>
    /// <param name="value">The raw value.</param>
    /// <returns>The code, or <see cref="Invalid"/>.</returns>
    public static ErrorOr<SubdivisionCode> Create(string? value)
    {
        string code = value?.Trim() ?? string.Empty;
        if (!Ascii.IsValid(code))
        {
            return Invalid;
        }

        code = code.ToUpperInvariant();
        return IsValid(code) ? new SubdivisionCode(code) : Invalid;
    }

    /// <inheritdoc />
    public override string ToString() => Value;

    private static bool IsValid(string code)
    {
        if (code.Length <= CountryLength + 1 || code[CountryLength] != '-')
        {
            return false;
        }

        ReadOnlySpan<char> suffix = code.AsSpan(CountryLength + 1);
        return CountryCode.IsValid(code.AsSpan(0, CountryLength))
            && suffix.Length <= MaxSuffixLength
            && !suffix.ContainsAnyExcept(_alphabet);
    }
}
