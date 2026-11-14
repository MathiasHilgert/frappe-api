using Frappe.Localization;

namespace Frappe.Geo.Infrastructure.Countries;

/// <summary>A row of <c>geo.country_names</c>: a country's name in one language.</summary>
internal sealed class CountryNameRow
{
    public required LanguageTag Language { get; init; }

    public required string Text { get; set; }
}
