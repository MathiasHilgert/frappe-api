using System.Buffers;
using System.Globalization;
using System.Text;

namespace Frappe.Geo.Contracts.ValueObjects;

/// <summary>The street part of an address, such as <c>Av. Corrientes 1234</c>; a house number is not required.</summary>
public sealed record StreetLine
{
    private const int MaxLength = 200;

    private StreetLine(string value) => Value = value;

    /// <summary>
    /// Gets the error returned for an empty or too long value, or one holding a control, format, line or paragraph
    /// separator character or a lone surrogate.
    /// </summary>
    public static Error Invalid { get; } = Error.Validation("geo.address.street_line_invalid");

    /// <summary>Gets the trimmed street line.</summary>
    public string Value { get; }

    /// <summary>Builds a street line from untrusted input: trimmed, then 1 to 200 characters without control, format or separator characters or lone surrogates.</summary>
    /// <param name="value">The raw value.</param>
    /// <returns>The street line, or <see cref="Invalid"/>.</returns>
    public static ErrorOr<StreetLine> Create(string? value)
    {
        string line = (value ?? string.Empty).Trim();
        return line.Length is > 0 and <= MaxLength && IsPrintable(line)
            ? new StreetLine(line)
            : Invalid;
    }

    /// <inheritdoc />
    public override string ToString() => Value;

    // Decodes whole code points, so a lone surrogate is invalid and a surrogate pair is one symbol.
    private static bool IsPrintable(ReadOnlySpan<char> line)
    {
        while (!line.IsEmpty)
        {
            if (
                Rune.DecodeFromUtf16(line, out Rune rune, out int used) != OperationStatus.Done
                || Rune.GetUnicodeCategory(rune)
                    is UnicodeCategory.Control
                        or UnicodeCategory.Format
                        or UnicodeCategory.LineSeparator
                        or UnicodeCategory.ParagraphSeparator
            )
            {
                return false;
            }

            line = line[used..];
        }

        return true;
    }
}
