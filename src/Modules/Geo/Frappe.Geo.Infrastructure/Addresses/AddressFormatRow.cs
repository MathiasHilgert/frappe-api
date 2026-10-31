namespace Frappe.Geo.Infrastructure.Addresses;

/// <summary>A row of <c>geo.address_formats</c>: a country's rules for capturing an address.</summary>
internal sealed class AddressFormatRow
{
    public string CountryCode { get; init; } = null!;

    /// <summary>Gets the postal-code pattern, unanchored as the source writes it; null accepts any code.</summary>
    public string? PostalCodePattern { get; init; }

    public string[] PostalCodeExamples { get; init; } = [];

    public bool PostalCodeRequired { get; init; }

    /// <summary>Gets the address parts the country requires, as their snake_case wire values, such as street_line and city.</summary>
    public string[] RequiredFields { get; init; } = [];

    /// <summary>Gets what the country calls its first-level subdivision, as its snake_case wire value, such as province.</summary>
    public string SubdivisionLabel { get; init; } = null!;
}
