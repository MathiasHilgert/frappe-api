using System.Collections.Frozen;
using Frappe.Geo.Application;
using Frappe.Geo.Application.Locales;
using Frappe.Geo.Contracts;
using Frappe.Geo.Contracts.ValueObjects;
using Frappe.Geo.Domain.Locales;
using Frappe.Geo.Infrastructure.Catalogues;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Geo.Infrastructure.Locales;

/// <summary>
/// The locale catalogue in memory, loaded once at startup: the active tags for the port, and the active rows ordered
/// by name once per seeded locale for the list.
/// </summary>
internal sealed class LocaleSnapshot : ILocaleReader, ILocaleCatalogue
{
    private Catalogue? _catalogue;

    private Catalogue Loaded =>
        _catalogue ?? throw new InvalidOperationException("The locale catalogue is not loaded.");

    public async Task<LoadedCatalogue> LoadAsync(
        GeoDbContext db,
        CancellationToken cancellationToken
    )
    {
        List<LocaleRow> rows = await db.Set<LocaleRow>()
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        List<LocaleNameRow> names = await db.Set<LocaleNameRow>()
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        FrozenDictionary<string, Labels> labels = Labels.ByKey(
            names,
            static name => name.LocaleTag,
            static name => name.Locale,
            static name => name.Name
        );
        LocaleEntry[] active =
        [
            .. rows.Where(static row => row.Status == ReferenceStatus.Active)
                .Select(row => new LocaleEntry(
                    Locale.Create(row.LocaleTag).Value,
                    Locale.Create(row.LanguageCode).Value,
                    string.Equals(row.TextDirection, "rtl", StringComparison.Ordinal)
                        ? TextDirection.Rtl
                        : TextDirection.Ltr,
                    row.Endonym,
                    Labels.Of(labels, "locale", row.LocaleTag)
                )),
        ];
        _catalogue = new Catalogue(
            active.Select(static entry => entry.Tag.Value).ToFrozenSet(StringComparer.Ordinal),
            Labels.OrderedByLocale(
                active,
                static entry => entry.Names,
                static entry => entry.Tag.Value,
                static (entry, name) =>
                    new LocaleDto(entry.Tag, entry.Language, entry.Direction, entry.Endonym, name)
            )
        );
        return new LoadedCatalogue(
            rows.Count,
            [
                .. rows.Select(static row =>
                        $"locale|{row.LocaleTag}|{row.LanguageCode}|{row.TextDirection}|{row.Endonym}|{row.Status}"
                    )
                    .Concat(
                        names.Select(static name =>
                            $"locale_name|{name.LocaleTag}|{name.Locale}|{name.Name}"
                        )
                    )
                    .Order(StringComparer.Ordinal),
            ]
        );
    }

    public Localized<IReadOnlyList<LocaleDto>> List(Locale locale) =>
        Labels.Pick(Loaded.Ordered, locale);

    public ValueTask<ErrorOr<Success>> Exists(Locale locale, CancellationToken cancellationToken) =>
        ValueTask.FromResult<ErrorOr<Success>>(
            Loaded.Active.Contains(locale.Value) ? Result.Success : LocaleErrors.NotFound
        );

    private sealed record LocaleEntry(
        Locale Tag,
        Locale Language,
        TextDirection Direction,
        string Endonym,
        Labels Names
    );

    private sealed record Catalogue(
        FrozenSet<string> Active,
        FrozenDictionary<string, IReadOnlyList<LocaleDto>> Ordered
    );
}
