using Frappe.SharedKernel.Localization;

namespace Frappe.Platform.Localization;

/// <summary>System strings (errors, mails, notifications) as ICU MessageFormat patterns, keyed and versioned with the code.</summary>
public interface IMessageCatalog
{
    /// <summary>
    /// Formats <paramref name="key"/> in <paramref name="locale"/>, falling back along
    /// <see cref="SupportedLocales.FallbackChain"/> when the locale lacks it.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The key is missing from the default locale: a programming error.</exception>
    string Format(Locale locale, string key, IReadOnlyDictionary<string, object?>? args = null);

    /// <summary>Like <see cref="Format"/>, but <see langword="null"/> when no locale in the chain has the key.</summary>
    string? TryFormat(Locale locale, string key, IReadOnlyDictionary<string, object?>? args = null);

    /// <summary>Whether the default locale defines <paramref name="key"/>.</summary>
    bool Contains(string key);
}
