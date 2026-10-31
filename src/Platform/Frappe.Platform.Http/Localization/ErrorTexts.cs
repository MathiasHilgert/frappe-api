using System.Collections.Frozen;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Frappe.Kernel.Localization;

namespace Frappe.Platform.Http.Localization;

/// <summary>
/// The translated error texts, keyed by error code: every <c>errors.&lt;locale&gt;.json</c> resource embedded in
/// the platform and in each module assembly.
/// </summary>
public sealed class ErrorTexts
{
    private const string ResourcePrefix = "errors.";
    private const string ResourceSuffix = ".json";

    private readonly FrozenDictionary<string, FrozenDictionary<string, string>> _byLocale;

    /// <summary>Loads the texts of <paramref name="assemblies"/>.</summary>
    /// <param name="assemblies">The assemblies that embed error texts; codes are owned, so none repeats across them.</param>
    public ErrorTexts(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        Dictionary<string, Dictionary<string, string>> byLocale = new(StringComparer.Ordinal);
        foreach (Assembly assembly in assemblies.Distinct())
        {
            foreach (string name in assembly.GetManifestResourceNames())
            {
                if (
                    !name.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                    || !name.EndsWith(ResourceSuffix, StringComparison.Ordinal)
                )
                {
                    continue;
                }

                string locale = name[ResourcePrefix.Length..^ResourceSuffix.Length];
                using Stream stream =
                    assembly.GetManifestResourceStream(name)
                    ?? throw new InvalidOperationException($"Resource {name} cannot be read.");
                Dictionary<string, string> texts =
                    JsonSerializer.Deserialize(
                        stream,
                        FrappeJsonContext.Default.DictionaryStringString
                    ) ?? [];
                Dictionary<string, string> target = byLocale.TryGetValue(
                    locale,
                    out Dictionary<string, string>? existing
                )
                    ? existing
                    : byLocale[locale] = new(StringComparer.Ordinal);
                foreach ((string code, string text) in texts)
                {
                    target[code] = text;
                }
            }
        }

        _byLocale = byLocale.ToFrozenDictionary(
            static entry => entry.Key,
            static entry => entry.Value.ToFrozenDictionary(StringComparer.Ordinal),
            StringComparer.Ordinal
        );
    }

    /// <summary>Gets whether <paramref name="code"/> has a text in exactly the locale <paramref name="localeTag"/>.</summary>
    /// <param name="code">The error code.</param>
    /// <param name="localeTag">The locale tag of the resource, such as <c>es</c>.</param>
    /// <returns>Whether the text exists, without any fallback.</returns>
    public bool Translates(string code, string localeTag) =>
        _byLocale.TryGetValue(localeTag, out FrozenDictionary<string, string>? texts)
        && texts.ContainsKey(code);

    /// <summary>
    /// Writes the text of <paramref name="error"/> in the first locale of <see cref="Locale.FallbackChain"/> that
    /// translates its code, else its English description; each <c>{name}</c> is replaced by that argument.
    /// </summary>
    /// <param name="error">The error.</param>
    /// <param name="locale">The requested locale.</param>
    /// <param name="args">The arguments the text refers to, or <see langword="null"/>.</param>
    /// <returns>The text.</returns>
    public string Render(
        Error error,
        Locale locale,
        IReadOnlyDictionary<string, object>? args = null
    )
    {
        ArgumentNullException.ThrowIfNull(locale);
        string text = error.Description;
        foreach (Locale candidate in locale.FallbackChain())
        {
            if (
                _byLocale.TryGetValue(candidate.Value, out FrozenDictionary<string, string>? texts)
                && texts.TryGetValue(error.Code, out string? found)
            )
            {
                text = found;
                break;
            }
        }

        // shortcut: only simple {name} arguments, no ICU plural or select; adopt an ICU MessageFormat renderer
        // when a message first needs them.
        foreach ((string name, object value) in args ?? FrozenDictionary<string, object>.Empty)
        {
            text = text.Replace(
                "{" + name + "}",
                Convert.ToString(value, CultureInfo.InvariantCulture),
                StringComparison.Ordinal
            );
        }

        return text;
    }
}
