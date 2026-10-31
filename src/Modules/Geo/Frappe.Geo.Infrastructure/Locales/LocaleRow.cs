using Frappe.Geo.Contracts;

namespace Frappe.Geo.Infrastructure.Locales;

/// <summary>A row of <c>geo.locales</c>: a CLDR locale with modern coverage.</summary>
internal sealed class LocaleRow
{
    public string LocaleTag { get; init; } = null!;

    public string LanguageCode { get; init; } = null!;

    public string TextDirection { get; init; } = null!;

    public string Endonym { get; init; } = null!;

    public ReferenceStatus Status { get; init; }
}
