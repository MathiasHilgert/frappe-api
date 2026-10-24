using Frappe.SharedKernel.Validation;

namespace Frappe.SharedKernel.Localization;

/// <summary>
/// A canonical BCP-47 language tag: language lowercase, script Titlecase, region UPPERCASE, so <c>es_ar</c>,
/// <c>es-ar</c> and <c>ES-AR</c> are all <c>es-AR</c>. Accepts <c>language[-script][-region](-variant)*</c>;
/// extensions and private-use subtags are rejected because nothing the platform serves depends on them.
/// </summary>
public sealed record Locale
{
    public const string InvalidCode = "locale.invalid";

    private Locale(string value, string language)
    {
        Value = value;
        Language = language;
    }

    /// <summary>The canonical tag, e.g. <c>es-AR</c>.</summary>
    public string Value { get; }

    /// <summary>The base language subtag, e.g. <c>es</c>: what the fallback chain matches on.</summary>
    public string Language { get; }

    public static Result<Locale> Create(string? tag)
    {
        var canonical = Canonicalise(tag);
        var notification = new ValidationNotification().AddIf(canonical is null,
            new Error(InvalidCode, "The locale must be a valid BCP-47 language tag, e.g. es-AR.", "locale"));
        return Result.From(notification, () => new Locale(canonical!, canonical!.Split('-')[0]));
    }

    /// <summary>For trusted input such as configuration; throws on an invalid tag.</summary>
    public static Locale Parse(string tag)
    {
        var result = Create(tag);
        return result.TryGetValue(out var locale) ? locale : throw new FormatException($"'{tag}' is not a valid BCP-47 language tag.");
    }

    public override string ToString() => Value;

    private static string? Canonicalise(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return null;

        var subtags = tag.Trim().Replace('_', '-').Split('-');
        var language = subtags[0];
        if (!IsLanguage(language))
            return null;

        var parts = new List<string>(subtags.Length) { language.ToLowerInvariant() };
        var i = 1;
        if (i < subtags.Length && IsScript(subtags[i]))
            parts.Add(char.ToUpperInvariant(subtags[i][0]) + subtags[i++][1..].ToLowerInvariant());
        if (i < subtags.Length && IsRegion(subtags[i]))
            parts.Add(subtags[i++].ToUpperInvariant());
        for (; i < subtags.Length; i++)
        {
            if (!IsVariant(subtags[i]))
                return null;
            parts.Add(subtags[i].ToLowerInvariant());
        }

        return string.Join('-', parts);
    }

    private static bool IsLanguage(string s) => s.Length is 2 or 3 or (>= 5 and <= 8) && s.All(char.IsAsciiLetter);

    private static bool IsScript(string s) => s.Length == 4 && s.All(char.IsAsciiLetter);

    private static bool IsRegion(string s) =>
        (s.Length == 2 && s.All(char.IsAsciiLetter)) || (s.Length == 3 && s.All(char.IsAsciiDigit));

    private static bool IsVariant(string s) =>
        s.All(char.IsAsciiLetterOrDigit)
        && (s.Length is >= 5 and <= 8 || (s.Length == 4 && char.IsAsciiDigit(s[0])));
}
