namespace Frappe.Geo.Application.Countries;

/// <summary>A country's rules for capturing an address, after Google's libaddressinput metadata.</summary>
/// <param name="PostalCodePattern">The regular expression a whole postal code must match, or null when any code is accepted.</param>
/// <param name="PostalCodeExamples">Valid postal codes, such as C1070AAM.</param>
/// <param name="PostalCodeRequired">Whether an address must carry a postal code.</param>
/// <param name="RequiredFields">The address parts the country requires, such as street_line and city.</param>
/// <param name="SubdivisionLabel">What the country calls its first-level subdivision, such as province.</param>
public sealed record AddressFormatDto(
    string? PostalCodePattern,
    IReadOnlyList<string> PostalCodeExamples,
    bool PostalCodeRequired,
    IReadOnlyList<AddressField> RequiredFields,
    SubdivisionLabel SubdivisionLabel
);
