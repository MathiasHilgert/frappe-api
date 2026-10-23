namespace Frappe.Platform.Localization;

/// <summary>Bound from the <c>Localization</c> section. Unset values fall back to the platform defaults.</summary>
public sealed class LocalizationOptions
{
    public const string SectionName = "Localization";

    /// <summary>Platform locales in preference order; order breaks ties between regions of one language.</summary>
    public static readonly IReadOnlyList<string> DefaultSupportedLocales = ["es-AR", "en", "es-MX", "pt-BR"];

    public const string DefaultLocaleTag = "en";

    public IList<string> SupportedLocales { get; } = [];

    public string? DefaultLocale { get; set; }
}
