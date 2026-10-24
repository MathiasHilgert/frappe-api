using System.Globalization;
using Frappe.SharedKernel.Localization;
using Jeffijoe.MessageFormat;

namespace Frappe.Platform.Localization;

/// <summary>Embedded JSON catalogues formatted with Jeffijoe.MessageFormat; the library stays behind <see cref="IMessageCatalog"/>.</summary>
internal sealed class MessageCatalog : IMessageCatalog
{
    private readonly SupportedLocales _supported;
    private readonly Dictionary<Locale, Dictionary<string, string>> _messages = [];

    public MessageCatalog(SupportedLocales supported, IEnumerable<MessageCatalogSource> sources)
    {
        _supported = supported;
        foreach (var source in sources)
        {
            foreach (var (locale, entries) in source.Load())
                Merge(locale, entries, source);
        }
    }

    public string Format(Locale locale, string key, IReadOnlyDictionary<string, object?>? args = null) =>
        TryFormat(locale, key, args)
        ?? throw new KeyNotFoundException($"Message '{key}' is missing from the default locale '{_supported.Default}'.");

    public bool Contains(string key) =>
        _messages.TryGetValue(_supported.Default, out var entries) && entries.ContainsKey(key);

    public string? TryFormat(Locale locale, string key, IReadOnlyDictionary<string, object?>? args = null)
    {
        foreach (var candidate in _supported.FallbackChain(locale))
        {
            if (_messages.TryGetValue(candidate, out var entries) && entries.TryGetValue(key, out var pattern))
                return Render(candidate, pattern, args ?? new Dictionary<string, object?>(StringComparer.Ordinal));
        }

        return null;
    }

    internal static string Render(Locale locale, string pattern, IReadOnlyDictionary<string, object?> args)
    {
        // MessageFormatter caches parsed patterns and is not documented as thread-safe: one per call keeps it simple.
        var formatter = new MessageFormatter(useCache: false, CultureInfo.GetCultureInfo(locale.Value));
        return formatter.FormatMessage(pattern, args);
    }

    private void Merge(Locale locale, IReadOnlyDictionary<string, string> entries, MessageCatalogSource source)
    {
        if (!_messages.TryGetValue(locale, out var target))
            _messages[locale] = target = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (key, pattern) in entries)
        {
            if (!target.TryAdd(key, pattern))
            {
                throw new InvalidOperationException(
                    $"Message '{key}' ({locale}) from {source.Assembly.GetName().Name} is already defined by another catalogue.");
            }
        }
    }
}
