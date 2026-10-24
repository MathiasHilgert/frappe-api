using System.ComponentModel.DataAnnotations;

namespace Frappe.Platform.Localization;

/// <summary>Bound from the <c>Localization</c> section; the defaults below apply to whatever is not set.</summary>
public sealed class LocalizationOptions : IValidatableObject
{
    public const string SectionName = "Localization";

    /// <summary>The platform's own defaults, also what catalogue conformance checks run against.</summary>
    public static SupportedLocales Defaults => new(new LocalizationOptions().SupportedLocales, new LocalizationOptions().DefaultLocale);

    /// <summary>Platform locales in preference order; order breaks ties between regions of one language.</summary>
    [MinLength(1)]
    public string[] SupportedLocales { get; set; } = ["es-AR", "en", "es-MX", "pt-BR"];

    [Required]
    public string DefaultLocale { get; set; } = "en";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!SupportedLocales.Contains(DefaultLocale, StringComparer.OrdinalIgnoreCase))
        {
            yield return new ValidationResult(
                $"'{SectionName}:DefaultLocale' must be one of the supported locales.", [nameof(DefaultLocale)]);
        }
    }
}
