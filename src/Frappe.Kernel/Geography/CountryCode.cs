namespace Frappe.Kernel.Geography;

/// <summary>An ISO 3166-1 alpha-2 country code in upper case, such as <c>AR</c>.</summary>
/// <remarks>Checks the format only; whether the country exists is the geo catalogue's decision.</remarks>
public sealed record CountryCode
{
    private const int Length = 2;

    private CountryCode(string value) => Value = value;

    /// <summary>Gets the error returned for a value that is not two ASCII letters.</summary>
    public static Error Invalid { get; } =
        Error.Validation(
            "kernel.country_code.invalid",
            "The country code must be two ISO 3166-1 alpha-2 letters."
        );

    /// <summary>Gets the upper-case code.</summary>
    public string Value { get; }

    /// <summary>Builds a country code from untrusted input, trimmed and upper-cased first.</summary>
    /// <param name="value">The raw value.</param>
    /// <returns>The code, or <see cref="Invalid"/>.</returns>
    public static ErrorOr<CountryCode> Create(string? value)
    {
        string code = value?.Trim().ToUpperInvariant() ?? string.Empty;
        return IsValid(code) ? new CountryCode(code) : Invalid;
    }

    /// <inheritdoc />
    public override string ToString() => Value;

    // Shared with SubdivisionCode, whose prefix is a country code.
    internal static bool IsValid(ReadOnlySpan<char> code) =>
        code.Length == Length
        && char.IsAsciiLetterUpper(code[0])
        && char.IsAsciiLetterUpper(code[1]);
}
