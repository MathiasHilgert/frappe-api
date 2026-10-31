using System.Buffers;
using System.Globalization;
using System.Text;

namespace Frappe.Kernel.Geography;

/// <summary>A structured street address in a geo city.</summary>
/// <remarks>Checks the street line only; the city, subdivision and postal pattern are the geo address validator's decision.</remarks>
public sealed record Address
{
    private const int MaxStreetLineLength = 200;

    private Address(CityId city, string streetLine, PostalCode? postalCode)
    {
        City = city;
        StreetLine = streetLine;
        PostalCode = postalCode;
    }

    /// <summary>Gets the error returned for an address without a city.</summary>
    public static Error CityRequired { get; } =
        Error.Validation("kernel.address.city_required", "The address must have a city.");

    /// <summary>Gets the error returned for an empty or too long street line, or one with invisible or malformed characters.</summary>
    public static Error Invalid { get; } =
        Error.Validation(
            "kernel.address.street_line_invalid",
            "The street line must be 1 to 200 characters with no control, formatting, separator or malformed characters."
        );

    /// <summary>Gets the city.</summary>
    public CityId City { get; }

    /// <summary>Gets the trimmed street line, such as <c>Av. Corrientes 1234</c>.</summary>
    public string StreetLine { get; }

    /// <summary>Gets the postal code, when one was given.</summary>
    public PostalCode? PostalCode { get; }

    // TODO: optional coordinates captured once from the chosen address suggestion (deferred with the geocoding provider; never inferred or backfilled)

    /// <summary>Builds an address from a city, an untrusted street line and an optional postal code.</summary>
    /// <param name="city">The city; the default id is rejected.</param>
    /// <param name="streetLine">The raw street line; trimmed first.</param>
    /// <param name="postalCode">The postal code, or <see langword="null"/>.</param>
    /// <returns>The address, or every error among <see cref="CityRequired"/> and <see cref="Invalid"/>.</returns>
    public static ErrorOr<Address> Create(CityId city, string? streetLine, PostalCode? postalCode)
    {
        string line = streetLine?.Trim() ?? string.Empty;
        List<Error> errors = [];
        if (city.Value == Guid.Empty)
        {
            errors.Add(CityRequired);
        }

        if (line.Length is 0 or > MaxStreetLineLength || HasInvisibleOrMalformedText(line))
        {
            errors.Add(Invalid);
        }

        return errors.Count == 0 ? new Address(city, line, postalCode) : errors;
    }

    // Controls, formatting characters (bidi overrides, zero-width), line or paragraph separators
    // and lone surrogates can hide or reorder what a person reads on a receipt or a label.
    private static bool HasInvisibleOrMalformedText(string line)
    {
        ReadOnlySpan<char> rest = line;
        while (!rest.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(rest, out Rune rune, out int consumed) != OperationStatus.Done)
            {
                return true;
            }

            if (
                Rune.GetUnicodeCategory(rune)
                is UnicodeCategory.Control
                    or UnicodeCategory.Format
                    or UnicodeCategory.LineSeparator
                    or UnicodeCategory.ParagraphSeparator
            )
            {
                return true;
            }

            rest = rest[consumed..];
        }

        return false;
    }
}
