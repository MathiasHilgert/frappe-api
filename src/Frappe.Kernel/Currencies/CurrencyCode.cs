using System.Text;

namespace Frappe.Kernel.Currencies;

/// <summary>An ISO 4217 alphabetic currency code in upper case, such as <c>ARS</c>.</summary>
/// <remarks>Checks the format only; whether the currency exists is the geo catalogue's decision.</remarks>
public sealed record CurrencyCode
{
    private const int Length = 3;

    private CurrencyCode(string value) => Value = value;

    /// <summary>Gets the error returned for a value that is not three ASCII letters.</summary>
    public static Error Invalid { get; } =
        Error.Validation(
            "kernel.currency_code.invalid",
            "The currency code must be three ISO 4217 letters."
        );

    /// <summary>Gets the upper-case code.</summary>
    public string Value { get; }

    /// <summary>Builds a currency code from untrusted input, trimmed and upper-cased first.</summary>
    /// <param name="value">The raw value.</param>
    /// <returns>The code, or <see cref="Invalid"/>.</returns>
    public static ErrorOr<CurrencyCode> Create(string? value)
    {
        string code = value?.Trim() ?? string.Empty;
        if (!Ascii.IsValid(code))
        {
            return Invalid;
        }

        code = code.ToUpperInvariant();
        return code.Length == Length && code.All(char.IsAsciiLetterUpper)
            ? new CurrencyCode(code)
            : Invalid;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
