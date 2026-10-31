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

    /// <summary>Gets the error returned for an empty, too long or control-character street line.</summary>
    public static Error Invalid { get; } =
        Error.Validation(
            "kernel.address.street_line_invalid",
            "The street line must be 1 to 200 characters with no control characters."
        );

    /// <summary>Gets the city.</summary>
    public CityId City { get; }

    /// <summary>Gets the trimmed street line, such as <c>Av. Corrientes 1234</c>.</summary>
    public string StreetLine { get; }

    /// <summary>Gets the postal code, when one was given.</summary>
    public PostalCode? PostalCode { get; }

    // TODO: optional coordinates captured once from the chosen address suggestion (deferred with the geocoding provider; never inferred or backfilled)

    /// <summary>Builds an address from a city, an untrusted street line and an optional postal code.</summary>
    /// <param name="city">The city.</param>
    /// <param name="streetLine">The raw street line; trimmed first.</param>
    /// <param name="postalCode">The postal code, or <see langword="null"/>.</param>
    /// <returns>The address, or <see cref="Invalid"/>.</returns>
    public static ErrorOr<Address> Create(CityId city, string? streetLine, PostalCode? postalCode)
    {
        string line = streetLine?.Trim() ?? string.Empty;
        return line.Length is > 0 and <= MaxStreetLineLength && !line.Any(char.IsControl)
            ? new Address(city, line, postalCode)
            : Invalid;
    }
}
