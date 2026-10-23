using Frappe.SharedKernel.Localization;

namespace Frappe.Platform.Localization;

/// <summary>The locales the platform itself speaks (system strings, mails, errors) and how it falls back between them.</summary>
public sealed class SupportedLocales
{
    public SupportedLocales(IEnumerable<string> tags, string defaultTag)
    {
        All = tags.Select(Locale.Parse).Distinct().ToList();
        Default = Locale.Parse(defaultTag);
        if (!All.Contains(Default))
            throw new InvalidOperationException($"The default locale '{Default}' must be one of the supported locales.");
    }

    /// <summary>In configured order.</summary>
    public IReadOnlyList<Locale> All { get; }

    public Locale Default { get; }

    /// <summary>
    /// RFC 4647 lookup of one requested tag: the exact tag, then its truncations (<c>zh-Hant-TW</c> → <c>zh-Hant</c>
    /// → <c>zh</c>), then the first configured locale of the same language (<c>es-CL</c> → <c>es-AR</c>).
    /// </summary>
    public Locale? Lookup(Locale requested)
    {
        var subtags = requested.Value.Split('-');
        for (var length = subtags.Length; length > 0; length--)
        {
            var candidate = string.Join('-', subtags[..length]);
            var match = All.FirstOrDefault(l => string.Equals(l.Value, candidate, StringComparison.Ordinal));
            if (match is not null)
                return match;
        }

        return All.FirstOrDefault(l => string.Equals(l.Language, requested.Language, StringComparison.Ordinal));
    }

    /// <summary>Where a message missing in <paramref name="locale"/> is looked for next, ending at the default.</summary>
    public IReadOnlyList<Locale> FallbackChain(Locale locale)
    {
        var chain = new List<Locale> { locale };
        chain.AddRange(All.Where(l => l != locale && string.Equals(l.Language, locale.Language, StringComparison.Ordinal)));
        if (!chain.Contains(Default))
            chain.Add(Default);
        return chain;
    }
}
