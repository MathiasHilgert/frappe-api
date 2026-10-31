using System.Buffers;
using System.Text;

namespace Frappe.Kernel.Geography;

/// <summary>A postal code in upper case with single inner spaces, such as <c>C1425DQF</c> or <c>SW1A 1AA</c>.</summary>
/// <remarks>Checks the shared alphabet only; each country's pattern is the geo address format's decision.</remarks>
public sealed record PostalCode
{
    private const int MaxLength = 16;

    private static readonly SearchValues<char> _alphabet = SearchValues.Create(
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 -"
    );

    private PostalCode(string value) => Value = value;

    /// <summary>Gets the error returned for an empty, too long or out-of-alphabet value.</summary>
    public static Error Invalid { get; } =
        Error.Validation(
            "kernel.postal_code.invalid",
            "The postal code must be 1 to 16 letters, digits, spaces or hyphens."
        );

    /// <summary>Gets the normalized code.</summary>
    public string Value { get; }

    /// <summary>Builds a postal code from untrusted input: trimmed, inner whitespace collapsed to one space, then upper-cased once it is ASCII.</summary>
    /// <param name="value">The raw value.</param>
    /// <returns>The code, or <see cref="Invalid"/>.</returns>
    public static ErrorOr<PostalCode> Create(string? value)
    {
        string code = string.Join(
            ' ',
            (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
        );
        if (!Ascii.IsValid(code))
        {
            return Invalid;
        }

        code = code.ToUpperInvariant();
        return code.Length is > 0 and <= MaxLength && !code.AsSpan().ContainsAnyExcept(_alphabet)
            ? new PostalCode(code)
            : Invalid;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
