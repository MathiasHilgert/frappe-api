using Frappe.Localization;

namespace Frappe.Notifications.Channels.Email;

/// <summary>
/// Finds the texts of a kind in the language to serve (S8). A language Frappé is written in follows the platform rule (<c>es-MX</c> →
/// <c>es</c>, <c>pt-PT</c> → <c>pt-BR</c>, <c>en-GB</c> → <c>en</c>); one it is not written in, or none, gets the default
/// <see cref="NotificationLanguages.Default"/>. A kind missing in the language asked for falls to the default before English. The language
/// served is the one of the texts returned.
/// </summary>
public sealed class IcuNotificationTexts(IEnumerable<INotificationTextSource> sources)
{
    private readonly INotificationTextSource[] _sources = [.. sources];

    /// <summary>The texts of <paramref name="kind"/> for <paramref name="requested"/>.</summary>
    /// <exception cref="InvalidOperationException">No source has texts for the kind in any language of the chain.</exception>
    public NotificationTexts For(string kind, LanguageTag? requested)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        var own = requested is not null && LanguageFallback.IsServed(requested)
            ? LanguageFallback.For(requested).Where(language => language != LanguageFallback.English || requested.LookupChain().Contains(language))
            : [];
        foreach (var language in own.Append(NotificationLanguages.Default).Append(LanguageFallback.English).Distinct())
        {
            var patterns = _sources.Select(source => source.Find(kind, language)).FirstOrDefault(found => found is not null);
            if (patterns is not null)
            {
                return new NotificationTexts(kind, language, patterns);
            }
        }

        throw new InvalidOperationException($"No texts for notification '{kind}' in {requested?.Value ?? "the default language"}.");
    }
}
