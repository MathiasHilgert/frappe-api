namespace Frappe.Localization;

/// <summary>
/// The platform language rule, shared by message texts and translated data. Frappé is written in English, Spanish
/// (a common Spanish plus Argentina and Mexico) and Brazilian Portuguese. A request is looked up from its own tag down
/// to its base language, then in the preferred variant of that language, then in English:
/// <c>es-CL</c> → <c>es</c> → <c>en</c>; <c>pt-PT</c> → <c>pt</c> → <c>pt-BR</c> → <c>en</c>.
/// </summary>
public static class LanguageFallback
{
    public static readonly LanguageTag English = LanguageTag.Create("en").Value;

    /// <summary>The languages Frappé is written in.</summary>
    public static IReadOnlyList<LanguageTag> Supported { get; } =
        new[] { "en", "es", "es-AR", "es-MX", "pt-BR" }.Select(tag => LanguageTag.Create(tag).Value).ToArray();

    /// <summary>
    /// The variant that answers for a language Frappé has no common text for: there is no common Portuguese, so any
    /// Portuguese request is answered in Brazilian Portuguese.
    /// </summary>
    private static readonly Dictionary<string, LanguageTag> PreferredVariant = new(StringComparer.Ordinal)
    {
        ["pt"] = LanguageTag.Create("pt-BR").Value,
    };

    /// <summary>The languages to try for <paramref name="requested"/>, in order, without repetitions.</summary>
    public static IReadOnlyList<LanguageTag> For(LanguageTag requested)
    {
        ArgumentNullException.ThrowIfNull(requested);

        var chain = OwnChain(requested);
        chain.Add(English);
        return chain.Distinct().ToArray();
    }

    /// <summary>
    /// The translation to serve for <paramref name="requested"/> among <paramref name="translations"/> (at most one per
    /// language): the first language of <see cref="For"/> that exists, else the first language that exists (ordinal), so
    /// data written in a single language is still served. <see langword="null"/> only when there is no translation.
    /// Texts that travel together (a currency's name and symbol) are one translation, so they never mix languages.
    /// </summary>
    public static T? Pick<T>(IEnumerable<T> translations, Func<T, LanguageTag> languageOf, LanguageTag requested)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(translations);
        ArgumentNullException.ThrowIfNull(languageOf);
        ArgumentNullException.ThrowIfNull(requested);

        var byLanguage = translations.ToDictionary(languageOf);
        foreach (var candidate in For(requested))
        {
            if (byLanguage.TryGetValue(candidate, out var translation))
            {
                return translation;
            }
        }

        return byLanguage.Count == 0 ? null : byLanguage.MinBy(entry => entry.Key.Value, StringComparer.Ordinal).Value;
    }

    /// <summary>
    /// Whether Frappé is written in <paramref name="requested"/> or in a language it narrows to (<c>es-CL</c> → <c>es</c>,
    /// <c>pt-PT</c> → <c>pt-BR</c>), without falling back to English as the last resort: <c>fr</c> is not served.
    /// </summary>
    public static bool IsServed(LanguageTag requested)
    {
        ArgumentNullException.ThrowIfNull(requested);
        return OwnChain(requested).Any(Supported.Contains);
    }

    /// <summary>The requested tag, its shorter tags and the preferred variant of its language, without English.</summary>
    private static List<LanguageTag> OwnChain(LanguageTag requested)
    {
        var chain = requested.LookupChain().ToList();
        if (PreferredVariant.TryGetValue(chain[^1].Value, out var variant))
        {
            chain.Add(variant);
        }

        return chain;
    }
}
