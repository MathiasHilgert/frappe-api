using Frappe.Geo.Contracts.ValueObjects;

namespace Frappe.Geo.Application.Locales;

/// <summary>A CLDR locale with modern coverage, such as es-AR.</summary>
/// <param name="LocaleTag">The canonical BCP 47 tag, such as es-AR.</param>
/// <param name="LanguageCode">The primary language subtag, such as es.</param>
/// <param name="TextDirection">The direction its script is written in.</param>
/// <param name="Endonym">The locale's name in itself, such as español (Argentina).</param>
/// <param name="Name">The locale's name in the requested locale, or the nearest one along its fallback chain.</param>
public sealed record LocaleDto(
    Locale LocaleTag,
    Locale LanguageCode,
    TextDirection TextDirection,
    string Endonym,
    string Name
);
