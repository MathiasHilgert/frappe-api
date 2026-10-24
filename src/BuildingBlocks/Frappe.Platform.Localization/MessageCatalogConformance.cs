using System.Reflection;
using Frappe.SharedKernel.Localization;

namespace Frappe.Platform.Localization;

/// <summary>
/// A check any module's tests can run against its own catalogue: every key of the default locale exists in every
/// supported locale, and every message formats. Returns the problems so the test can print all of them at once.
/// </summary>
public static class MessageCatalogConformance
{
    /// <summary>Plural branches differ by count, so each pattern is rendered with several numbers.</summary>
    private static readonly int[] SampleValues = [0, 1, 2, 5];

    /// <summary>
    /// The error codes with no entry in the default locale of <paramref name="catalogue"/>; such errors still answer
    /// with their English <c>Error.Message</c>, untranslated.
    /// </summary>
    public static IReadOnlyList<string> CodesWithoutEntries(Assembly catalogue, IEnumerable<string> codes)
    {
        var supported = LocalizationOptions.Defaults;
        var english = new MessageCatalogSource(catalogue).Load().GetValueOrDefault(supported.Default);
        return codes.Distinct(StringComparer.Ordinal).Where(c => english?.ContainsKey(c) != true).ToList();
    }

    public static IReadOnlyList<string> Check(Assembly assembly, SupportedLocales? supported = null) =>
        Check(new MessageCatalogSource(assembly).Load(),
            supported ?? LocalizationOptions.Defaults);

    internal static IReadOnlyList<string> Check(
        IReadOnlyDictionary<Locale, IReadOnlyDictionary<string, string>> catalogues, SupportedLocales supported)
    {
        var problems = new List<string>();

        if (!catalogues.TryGetValue(supported.Default, out var reference))
            return [$"No catalogue for the default locale '{supported.Default}'."];

        foreach (var locale in supported.All)
        {
            if (!catalogues.TryGetValue(locale, out var entries))
            {
                problems.Add($"No catalogue for '{locale}'.");
                continue;
            }

            problems.AddRange(reference.Keys.Where(k => !entries.ContainsKey(k)).Select(k => $"'{k}' is missing in '{locale}'."));
            problems.AddRange(entries.SelectMany(e => Unformattable(locale, e.Key, e.Value)));
        }

        return problems;
    }

    private static IEnumerable<string> Unformattable(Locale locale, string key, string pattern)
    {
        foreach (var value in SampleValues)
        {
            string? error = null;
            try
            {
                MessageCatalog.Render(locale, pattern, new AnyArgument(value));
            }
#pragma warning disable CA1031 // Any failure is a broken message to report, not to crash on.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                error = $"'{key}' in '{locale}' does not format: {ex.Message}";
            }

            if (error is not null)
            {
                yield return error;
                yield break;
            }
        }
    }

    /// <summary>Answers every argument name with the same number, so patterns can be rendered without knowing their arguments.</summary>
    private sealed class AnyArgument(int value) : IReadOnlyDictionary<string, object?>
    {
        public object? this[string key] => value;
        public IEnumerable<string> Keys => [];
        public IEnumerable<object?> Values => [];
        public int Count => 0;
        public bool ContainsKey(string key) => true;

        public bool TryGetValue(string key, out object? result)
        {
            result = value;
            return true;
        }

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => Enumerable.Empty<KeyValuePair<string, object?>>().GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
