namespace Frappe.Geo.Infrastructure.Locales;

/// <summary>A row of <c>geo.locale_names</c>: a locale's name in one locale.</summary>
internal sealed class LocaleNameRow
{
    public string LocaleTag { get; init; } = null!;

    public string Locale { get; init; } = null!;

    public string Name { get; init; } = null!;
}
