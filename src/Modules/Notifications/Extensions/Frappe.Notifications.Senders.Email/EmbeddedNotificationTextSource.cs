using System.Reflection;
using System.Text.Json;
using Frappe.Localization;

namespace Frappe.Notifications.Senders.Email;

/// <summary>
/// The texts embedded in an assembly: one <c>Texts/&lt;language&gt;.json</c> per language, each <c>{ "kind": { "key": "pattern" } }</c>.
/// A plug-in embeds its own files and registers one source; nothing is read from disk at run time.
/// </summary>
public sealed class EmbeddedNotificationTextSource : INotificationTextSource
{
    /// <summary>The logical name prefix of the embedded files (set in the project file).</summary>
    public const string ResourcePrefix = "frappe.notification_texts/";

    private readonly Dictionary<LanguageTag, Dictionary<string, Dictionary<string, string>>> _byLanguage = [];

    public EmbeddedNotificationTextSource(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        foreach (var resource in assembly.GetManifestResourceNames().Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)))
        {
            var tag = Path.GetFileNameWithoutExtension(resource[ResourcePrefix.Length..]);
            var language = LanguageTag.Create(tag);
            if (!language.IsSuccess)
            {
                throw new InvalidOperationException($"The embedded notification texts '{resource}' are not named after a language tag.");
            }

            using var stream = assembly.GetManifestResourceStream(resource)!;
            _byLanguage[language.Value] = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(stream)
                ?? throw new InvalidOperationException($"The embedded notification texts '{resource}' are empty.");
        }
    }

    /// <summary>The languages that have a file.</summary>
    public IReadOnlyCollection<LanguageTag> Languages => _byLanguage.Keys;

    /// <inheritdoc />
    public IReadOnlyCollection<string> KindsOf(LanguageTag language) =>
        _byLanguage.TryGetValue(language, out var kinds) ? kinds.Keys : [];

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string>? Find(string kind, LanguageTag language) =>
        _byLanguage.TryGetValue(language, out var kinds) && kinds.TryGetValue(kind, out var patterns) ? patterns : null;
}
