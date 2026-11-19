using System.Reflection;
using System.Text.Json;
using Frappe.Localization;

namespace Frappe.Http;

/// <summary>
/// The messages contributed by each module's HTTP adapter: one JSON file per language in <c>Resources/Messages</c>
/// (<c>en.json</c>, <c>es.json</c>, <c>es-AR.json</c>, ...), embedded in the assembly, mapping error codes to ICU
/// MessageFormat patterns.
/// </summary>
public sealed class ErrorMessageOptions
{
    /// <summary>Prefix of the embedded message files; set for every project in <c>Directory.Build.props</c>.</summary>
    internal const string ResourcePrefix = "frappe.messages/";

    internal Dictionary<LanguageTag, Dictionary<string, string>> PatternsByLanguage { get; } = [];

    /// <summary>
    /// Adds the messages embedded in a module's assembly. Every file must be a supported language, every key must
    /// exist in English (the last fallback) and belong to one module only, so a typo or a clash fails at startup
    /// instead of in a response.
    /// </summary>
    public void Add(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var files = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal))
            .ToDictionary(name => name[ResourcePrefix.Length..^".json".Length], name => Read(assembly, name), StringComparer.Ordinal);
        Add(assembly.GetName().Name ?? "an assembly", files);
    }

    /// <summary>Adds one owner's messages, keyed by the language of each file.</summary>
    internal void Add(string owner, IReadOnlyDictionary<string, Dictionary<string, string>> filesByLanguage)
    {
        var files = filesByLanguage.ToDictionary(file => Language(owner, file.Key), file => file.Value);
        if (!files.TryGetValue(LanguageFallback.English, out var english))
        {
            throw new InvalidOperationException($"{owner} has no English messages ({ResourcePrefix}en.json).");
        }

        foreach (var (language, patterns) in files)
        {
            if (patterns.Keys.FirstOrDefault(key => !english.ContainsKey(key)) is { } untranslated)
            {
                throw new InvalidOperationException($"Message '{untranslated}' of {owner} exists in '{language}' but not in English.");
            }

            if (!PatternsByLanguage.TryGetValue(language, out var existing))
            {
                existing = [with(StringComparer.Ordinal)];
                PatternsByLanguage[language] = existing;
            }

            foreach (var (key, pattern) in patterns)
            {
                if (!existing.TryAdd(key, pattern))
                {
                    throw new InvalidOperationException($"Message '{key}' is registered twice for language '{language}'.");
                }
            }
        }
    }

    private static LanguageTag Language(string owner, string raw) =>
        LanguageTag.Create(raw) is { IsSuccess: true } tag && LanguageFallback.Supported.Contains(tag.Value)
            ? tag.Value
            : throw new InvalidOperationException(
                $"{owner} has messages for '{raw}', which is not a supported language ({string.Join(", ", LanguageFallback.Supported)}).");

    private static Dictionary<string, string> Read(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException($"{resourceName} of {assembly.GetName().Name} is empty.");
    }
}
