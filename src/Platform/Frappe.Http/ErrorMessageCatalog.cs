using System.Collections;
using System.Globalization;
using Frappe.Localization;
using Microsoft.Extensions.Options;

namespace Frappe.Http;

/// <summary>
/// Renders error codes as text following the platform language rule (<see cref="LanguageFallback.For"/>):
/// <c>es-AR</c>, then <c>es</c>, then English. Patterns are ICU MessageFormat, formatted by <see cref="IcuMessages"/>, so
/// plurals and numbers follow the rules of the language each text is written in, and the caller always learns which
/// language that was.
/// </summary>
public sealed class ErrorMessageCatalog(IOptions<ErrorMessageOptions> options)
{
    private readonly Dictionary<LanguageTag, Dictionary<string, string>> _patterns = options.Value.PatternsByLanguage;

    /// <summary>Returns the rendered text and the language it is written in, or <c>null</c> when no language has it.</summary>
    public (string Text, LanguageTag Language)? Render(string key, LanguageTag requestedLanguage, IReadOnlyDictionary<string, object?> parameters)
    {
        ArgumentNullException.ThrowIfNull(requestedLanguage);
        ArgumentNullException.ThrowIfNull(parameters);

        foreach (var candidate in LanguageFallback.For(requestedLanguage))
        {
            if (_patterns.TryGetValue(candidate, out var patterns) && patterns.TryGetValue(key, out var pattern))
            {
                return (IcuMessages.Format(pattern, Arguments(parameters, IcuMessages.CultureOf(candidate)), candidate), candidate);
            }
        }

        return null;
    }

    /// <summary>ICU has no list arguments: a sequence is joined in the culture of the message.</summary>
    private static Dictionary<string, object?> Arguments(IReadOnlyDictionary<string, object?> parameters, CultureInfo culture) =>
        parameters.ToDictionary(
            entry => entry.Key,
            entry => entry.Value is IEnumerable items and not string
                ? string.Join(", ", items.Cast<object?>().Select(item => Convert.ToString(item, culture)))
                : entry.Value,
            StringComparer.Ordinal);
}
