using System.Reflection;
using Frappe.Localization;

namespace Frappe.Notifications.Channels.Email;

/// <summary>
/// The texts of one notification kind in one language, as ICU MessageFormat patterns (S6). A kind reads <c>t["heading"]</c> for a plain
/// text and <c>t["subject", new { p.Code }]</c> for one with arguments: each property of the anonymous object is the argument of the same
/// name (<c>{Code}</c>). A missing key or a missing argument throws, so the test that renders every sample finds it.
/// </summary>
public sealed class NotificationTexts
{
    private readonly string _kind;
    private readonly IReadOnlyDictionary<string, string> _patterns;

    public NotificationTexts(string kind, LanguageTag language, IReadOnlyDictionary<string, string> patterns)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(patterns);
        _kind = kind;
        Language = language;
        _patterns = patterns;
    }

    /// <summary>The language the texts are written in (the one served, which may differ from the one asked for).</summary>
    public LanguageTag Language { get; }

    /// <summary>The text of <paramref name="key"/>, formatted with no arguments.</summary>
    /// <exception cref="KeyNotFoundException">The kind has no such text in this language.</exception>
    public string this[string key] => IcuMessages.Format(PatternOf(key), new Dictionary<string, object?>(), Language);

    /// <summary>The text of <paramref name="key"/>, formatted with the properties of <paramref name="arguments"/>.</summary>
    /// <exception cref="KeyNotFoundException">The kind has no such text in this language.</exception>
    public string this[string key, object arguments]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(arguments);
            var values = arguments.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .ToDictionary(property => property.Name, property => property.GetValue(arguments), StringComparer.Ordinal);
            return IcuMessages.Format(PatternOf(key), values, Language);
        }
    }

    public override string ToString() => $"NotificationTexts {{ Kind = {_kind}, Language = {Language.Value} }}";

    private string PatternOf(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _patterns.TryGetValue(key, out var pattern)
            ? pattern
            : throw new KeyNotFoundException($"Notification '{_kind}' has no text '{key}' in {Language.Value}.");
    }
}
