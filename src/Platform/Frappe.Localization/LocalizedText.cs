using Frappe.Results;

namespace Frappe.Localization;

/// <summary>A text written in one language.</summary>
public sealed record Translation(LanguageTag Language, string Text)
{
    public const int MaxLength = 200;
}

/// <summary>
/// A name that exists in one or more languages. Reading it follows the platform rule:
/// a request may narrow to its base language (<c>es-AR</c> → <c>es</c>), but never switches language.
/// </summary>
public sealed class LocalizedText : IEquatable<LocalizedText>
{
    private readonly Dictionary<LanguageTag, Translation> _byLanguage;

    private LocalizedText(IEnumerable<Translation> translations) =>
        _byLanguage = translations.ToDictionary(t => t.Language);

    public IReadOnlyCollection<Translation> Translations => _byLanguage.Values;

    public static Result<LocalizedText> Create(IEnumerable<(LanguageTag Language, string Text)> translations)
    {
        ArgumentNullException.ThrowIfNull(translations);

        var notification = new Notification();
        var accepted = new List<Translation>();
        var seen = new HashSet<LanguageTag>();
        foreach (var (language, raw) in translations)
        {
            var text = raw?.Trim() ?? string.Empty;
            if (!seen.Add(language))
            {
                notification.Add(Error.InvalidValue("localization.translation.duplicate_language", ("language", language.Value)));
            }
            else if (text.Length == 0)
            {
                notification.Add(Error.InvalidValue("localization.translation.blank", ("language", language.Value)));
            }
            else if (text.Length > Translation.MaxLength)
            {
                notification.Add(Error.InvalidValue(
                    "localization.translation.too_long", ("language", language.Value), ("max", Translation.MaxLength)));
            }
            else
            {
                accepted.Add(new Translation(language, text));
            }
        }

        if (!notification.HasErrors && accepted.Count == 0)
        {
            notification.Add(Error.InvalidValue("localization.text.empty"));
        }

        return notification.HasErrors ? notification.ToFailure<LocalizedText>() : new LocalizedText(accepted);
    }

    /// <summary>Rebuilds a text from translations that were validated when they were stored.</summary>
    public static LocalizedText FromStored(IEnumerable<Translation> translations)
    {
        ArgumentNullException.ThrowIfNull(translations);
        var text = new LocalizedText(translations);
        return text._byLanguage.Count > 0
            ? text
            : throw new InvalidOperationException("A stored localized text has no translations.");
    }

    /// <summary>The translation for <paramref name="requested"/>, following the RFC 4647 lookup chain.</summary>
    public Result<Translation> In(LanguageTag requested)
    {
        ArgumentNullException.ThrowIfNull(requested);

        foreach (var candidate in requested.LookupChain())
        {
            if (_byLanguage.TryGetValue(candidate, out var translation))
            {
                return translation;
            }
        }

        return Error.RuleViolation(
            "localization.translation.unavailable",
            ("requested", requested.Value),
            ("available", _byLanguage.Keys.Select(k => k.Value).Order(StringComparer.Ordinal).ToArray()));
    }

    public bool Equals(LocalizedText? other) =>
        other is not null
        && _byLanguage.Count == other._byLanguage.Count
        && _byLanguage.All(entry => other._byLanguage.TryGetValue(entry.Key, out var match) && match == entry.Value);

    public override bool Equals(object? obj) => Equals(obj as LocalizedText);

    public override int GetHashCode()
    {
        var hash = 0;
        foreach (var translation in _byLanguage.Values)
        {
            hash ^= translation.GetHashCode();
        }

        return hash;
    }
}
