using System.Collections.Frozen;
using Frappe.Geo.Application;
using Frappe.Geo.Application.Subdivisions;
using Frappe.Geo.Contracts;
using Frappe.Geo.Contracts.ValueObjects;
using Frappe.Geo.Infrastructure.Catalogues;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Geo.Infrastructure.Subdivisions;

/// <summary>
/// The subdivision catalogue in memory, loaded once at startup: every row for lookups, and each country's active rows
/// ordered by name once per seeded locale.
/// </summary>
internal sealed class SubdivisionSnapshot : ISubdivisionReader
{
    private Catalogue? _catalogue;

    private Catalogue Loaded =>
        _catalogue
        ?? throw new InvalidOperationException("The subdivision catalogue is not loaded.");

    public async Task<LoadedCatalogue> LoadAsync(
        GeoDbContext db,
        CancellationToken cancellationToken
    )
    {
        List<SubdivisionRow> rows = await db.Set<SubdivisionRow>()
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        List<SubdivisionNameRow> names = await db.Set<SubdivisionNameRow>()
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        FrozenDictionary<string, Labels> labels = Labels.ByKey(
            names,
            static name => name.SubdivisionCode,
            static name => name.Locale,
            static name => name.Name
        );
        Subdivision[] subdivisions =
        [
            .. rows.Select(row => new Subdivision(
                SubdivisionCode.Create(row.SubdivisionCode).Value,
                row.Status,
                labels[row.SubdivisionCode]
            )),
        ];
        _catalogue = new Catalogue(
            subdivisions.ToFrozenDictionary(
                static subdivision => subdivision.Code.Value,
                StringComparer.Ordinal
            ),
            subdivisions
                .Where(static subdivision => subdivision.Status == ReferenceStatus.Active)
                .GroupBy(
                    static subdivision => subdivision.Code.Country.Value,
                    StringComparer.Ordinal
                )
                .ToFrozenDictionary(
                    static country => country.Key,
                    static country =>
                        Labels.OrderedByLocale(
                            [.. country],
                            static subdivision => subdivision.Names,
                            static subdivision => subdivision.Code.Value,
                            static (subdivision, name) => new SubdivisionDto(subdivision.Code, name)
                        ),
                    StringComparer.Ordinal
                ),
            names
                .Select(static name => name.Locale)
                .Distinct(StringComparer.Ordinal)
                .ToFrozenDictionary(
                    static locale => locale,
                    static IReadOnlyList<SubdivisionDto> (_) => [],
                    StringComparer.Ordinal
                )
        );
        return new LoadedCatalogue(rows.Count, Lines(rows, names));
    }

    public Localized<IReadOnlyList<SubdivisionDto>> List(CountryCode country, Locale locale) =>
        Labels.Pick(
            Loaded.ActiveByCountry.GetValueOrDefault(country.Value, Loaded.NoneByLocale),
            locale
        );

    public Localized<SubdivisionDto>? Find(SubdivisionCode subdivision, Locale locale)
    {
        if (!Loaded.All.TryGetValue(subdivision.Value, out Subdivision? found))
        {
            return null;
        }

        Localized<string> name = found.Names.Localize(locale);
        return new Localized<SubdivisionDto>(
            new SubdivisionDto(found.Code, name.Value),
            name.Locale
        );
    }

    private static IReadOnlyList<string> Lines(
        IEnumerable<SubdivisionRow> rows,
        IEnumerable<SubdivisionNameRow> names
    ) =>
        [
            .. rows.Select(static row =>
                    $"subdivision|{row.SubdivisionCode}|{row.CountryCode}|{row.Status}"
                )
                .Concat(
                    names.Select(static name =>
                        $"subdivision_name|{name.SubdivisionCode}|{name.Locale}|{name.Name}"
                    )
                )
                .Order(StringComparer.Ordinal),
        ];

    private sealed record Subdivision(SubdivisionCode Code, ReferenceStatus Status, Labels Names);

    // A country without subdivisions still answers in the locale a seeded name would resolve to.
    private sealed record Catalogue(
        FrozenDictionary<string, Subdivision> All,
        FrozenDictionary<
            string,
            FrozenDictionary<string, IReadOnlyList<SubdivisionDto>>
        > ActiveByCountry,
        FrozenDictionary<string, IReadOnlyList<SubdivisionDto>> NoneByLocale
    );
}
