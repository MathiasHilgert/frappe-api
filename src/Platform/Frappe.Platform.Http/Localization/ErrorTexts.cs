using System.Collections.Frozen;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Frappe.Platform.Http.Localization;

/// <summary>
/// The translated error texts, keyed by error code: every <c>errors.&lt;locale&gt;.json</c> resource embedded in
/// the platform and in each module assembly. Built on the first error response; DI does not cache a singleton
/// whose factory threw, so a broken resource fails every error response (re-reading the assemblies each time,
/// and ending as an empty-body 500 on the exception-handler path). The architecture tests load every shipped
/// resource, so CI catches it before a deploy.
/// </summary>
public sealed class ErrorTexts
{
    private const string Prefix = "errors";
    private const string Suffix = ".json";

    private readonly FrozenDictionary<string, FrozenDictionary<string, string>> _byLocale;

    /// <summary>Loads the texts embedded in <paramref name="assemblies"/>.</summary>
    /// <param name="assemblies">The assemblies that embed error texts; codes are owned, so none repeats across them.</param>
    /// <exception cref="InvalidOperationException">A resource is misnamed or is not a JSON object of strings.</exception>
    public ErrorTexts(IEnumerable<Assembly> assemblies)
        : this(Resources(assemblies)) { }

    internal ErrorTexts(IEnumerable<(string Name, Func<Stream> Open)> resources)
    {
        Dictionary<string, Dictionary<string, string>> byLocale = new(StringComparer.Ordinal);
        foreach ((string name, Func<Stream> open) in resources)
        {
            if (
                !name.StartsWith(Prefix, StringComparison.Ordinal)
                || !name.EndsWith(Suffix, StringComparison.Ordinal)
            )
            {
                continue;
            }

            ErrorLocale locale = LocaleOf(name);
            Dictionary<string, string> target = byLocale.TryGetValue(
                locale.Value,
                out Dictionary<string, string>? existing
            )
                ? existing
                : byLocale[locale.Value] = new(StringComparer.Ordinal);
            foreach ((string code, string text) in Read(name, open))
            {
                target[code] = text;
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
    /// <param name="localeTag">The canonical locale tag of the resource, such as <c>es</c> or <c>pt-BR</c>.</param>
    /// <returns>Whether the text exists, without any fallback.</returns>
    public bool Translates(string code, string localeTag) =>
        _byLocale.TryGetValue(localeTag, out FrozenDictionary<string, string>? texts)
        && texts.ContainsKey(code);

    /// <summary>
    /// Writes the text of <paramref name="code"/> in the first locale of <see cref="ErrorLocale.FallbackChain"/> that
    /// translates it; each <c>{name}</c> is replaced by that argument.
    /// </summary>
    /// <param name="code">The error code.</param>
    /// <param name="locale">The requested locale.</param>
    /// <param name="args">The arguments the text refers to, or <see langword="null"/>.</param>
    /// <returns>The text, or <see langword="null"/> when no locale of the chain translates the code.</returns>
    internal string? Render(
        string code,
        ErrorLocale locale,
        IReadOnlyDictionary<string, object>? args = null
    )
    {
        ArgumentNullException.ThrowIfNull(locale);
        string? text = null;
        foreach (ErrorLocale candidate in locale.FallbackChain())
        {
            if (
                _byLocale.TryGetValue(candidate.Value, out FrozenDictionary<string, string>? texts)
                && texts.TryGetValue(code, out text)
            )
            {
                break;
            }
        }

        // shortcut: only simple {name} arguments, no ICU plural or select; adopt an ICU MessageFormat renderer
        // when a message first needs them.
        foreach ((string name, object value) in args ?? FrozenDictionary<string, object>.Empty)
        {
            text = text?.Replace(
                "{" + name + "}",
                Convert.ToString(value, CultureInfo.InvariantCulture),
                StringComparison.Ordinal
            );
        }

        return text;
    }

    private static IEnumerable<(string Name, Func<Stream> Open)> Resources(
        IEnumerable<Assembly> assemblies
    )
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        return assemblies
            .Distinct()
            .SelectMany(static assembly =>
                assembly
                    .GetManifestResourceNames()
                    .Select(name =>
                        (
                            name,
                            (Func<Stream>)(
                                () =>
                                    assembly.GetManifestResourceStream(name)
                                    ?? throw new InvalidOperationException(
                                        $"Error texts resource {name} cannot be read."
                                    )
                            )
                        )
                    )
            )
            .ToArray();
    }

    // errors.pt-br.json serves pt-BR: the tag is canonicalized like any request locale.
    private static ErrorLocale LocaleOf(string name)
    {
        int start = Prefix.Length + 1;
        string tag =
            name.Length > start + Suffix.Length && name[Prefix.Length] == '.'
                ? name[start..^Suffix.Length]
                : "";
        return ErrorLocale.Parse(tag)
            ?? throw new InvalidOperationException(
                $"Error texts resource {name} must be named errors.<locale>.json with a BCP 47 locale, "
                    + "such as errors.es.json."
            );
    }

    private static Dictionary<string, string> Read(string name, Func<Stream> open)
    {
        try
        {
            using Stream stream = open();
            return JsonSerializer.Deserialize(
                    stream,
                    FrappeJsonContext.Default.DictionaryStringString
                ) ?? throw new InvalidOperationException($"Error texts resource {name} is null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Error texts resource {name} must be a JSON object of code-to-text strings.",
                exception
            );
        }
    }
}
