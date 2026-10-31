using System.Collections.Frozen;
using System.Globalization;
using Frappe.Geo.Application;
using Frappe.Geo.Contracts.ValueObjects;

namespace Frappe.Geo.Infrastructure.Catalogues;

/// <summary>The translated names of one reference row, by canonical locale tag.</summary>
/// <param name="names">The names; English is always seeded.</param>
internal sealed class Labels(FrozenDictionary<string, string> names)
{
    /// <summary>Gets the locale tags the row has a name in.</summary>
    public IEnumerable<string> Locales => names.Keys;

    /// <summary>
    /// Returns the list of the first locale along the fallback chain of <paramref name="locale"/> that has one, so a
    /// request never sorts; empty when nothing is loaded.
    /// </summary>
    public static Localized<IReadOnlyList<TDto>> Pick<TDto>(
        FrozenDictionary<string, IReadOnlyList<TDto>> listsByLocale,
        Locale locale
    )
    {
        foreach (Locale tag in locale.FallbackChain())
        {
            if (listsByLocale.TryGetValue(tag.Value, out IReadOnlyList<TDto>? list))
            {
                return new Localized<IReadOnlyList<TDto>>(list, tag);
            }
        }

        return new Localized<IReadOnlyList<TDto>>([], Locale.English);
    }

    /// <summary>
    /// Builds one list per seeded locale, ordered by the name in that locale with its culture's comparer and then by
    /// code, so a request picks a ready list instead of sorting.
    /// </summary>
    public static FrozenDictionary<string, IReadOnlyList<TDto>> OrderedByLocale<TRow, TDto>(
        IReadOnlyCollection<TRow> rows,
        Func<TRow, Labels> labels,
        Func<TRow, string> code,
        Func<TRow, string, TDto> toDto
    )
    {
        Dictionary<string, IReadOnlyList<TDto>> lists = new(StringComparer.Ordinal);
        foreach (
            string tag in rows.SelectMany(row => labels(row).Locales)
                .Distinct(StringComparer.Ordinal)
        )
        {
            Locale locale = Locale.Create(tag).Value;
            var byName = StringComparer.Create(new CultureInfo(tag), ignoreCase: true);
            lists[tag] =
            [
                .. rows.Select(row => (Row: row, Name: labels(row).Resolve(locale)))
                    .OrderBy(static named => named.Name, byName)
                    .ThenBy(named => code(named.Row), StringComparer.Ordinal)
                    .Select(named => toDto(named.Row, named.Name)),
            ];
        }

        return lists.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>Groups name rows by the key of the row they name.</summary>
    public static FrozenDictionary<string, Labels> ByKey<TName>(
        IEnumerable<TName> names,
        Func<TName, string> key,
        Func<TName, string> locale,
        Func<TName, string> name
    ) =>
        names
            .GroupBy(key, StringComparer.Ordinal)
            .ToFrozenDictionary(
                static group => group.Key,
                group => new Labels(group.ToFrozenDictionary(locale, name, StringComparer.Ordinal)),
                StringComparer.Ordinal
            );

    /// <summary>Returns the name in <paramref name="locale"/>, else the first one along its fallback chain.</summary>
    public string Resolve(Locale locale) => Localize(locale).Value;

    /// <summary>Returns the name along the fallback chain of <paramref name="locale"/> with the locale it is in.</summary>
    public Localized<string> Localize(Locale locale)
    {
        foreach (Locale tag in locale.FallbackChain())
        {
            if (names.TryGetValue(tag.Value, out string? name))
            {
                return new Localized<string>(name, tag);
            }
        }

        throw new InvalidOperationException("A reference row has no English name.");
    }
}
