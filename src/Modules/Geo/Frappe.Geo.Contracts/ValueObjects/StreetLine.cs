using System.Globalization;

namespace Frappe.Geo.Contracts.ValueObjects;

/// <summary>The street part of an address, such as <c>Av. Corrientes 1234</c>; a house number is not required.</summary>
public sealed record StreetLine
{
    private const int MaxLength = 200;

    private StreetLine(string value) => Value = value;

    /// <summary>Gets the error returned for an empty or too long value, or one holding a control or format character.</summary>
    public static Error Invalid { get; } = Error.Validation("geo.address.street_line_invalid");

    /// <summary>Gets the trimmed street line.</summary>
    public string Value { get; }

    /// <summary>Builds a street line from untrusted input: trimmed, then 1 to 200 characters without control or format characters.</summary>
    /// <param name="value">The raw value.</param>
    /// <returns>The street line, or <see cref="Invalid"/>.</returns>
    public static ErrorOr<StreetLine> Create(string? value)
    {
        string line = (value ?? string.Empty).Trim();
        return
            line.Length is > 0 and <= MaxLength
            && !line.Any(static character =>
                char.GetUnicodeCategory(character)
                    is UnicodeCategory.Control
                        or UnicodeCategory.Format
            )
            ? new StreetLine(line)
            : Invalid;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
