using System.Collections.Frozen;
using Frappe.Geo.Application;
using Frappe.Geo.Application.Currencies;
using Frappe.Geo.Contracts;
using Frappe.Geo.Contracts.ValueObjects;
using Frappe.Geo.Domain.Currencies;
using Frappe.Geo.Infrastructure.Catalogues;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Geo.Infrastructure.Currencies;

/// <summary>
/// The currency catalogue in memory, loaded once at startup: every row for the port, the active rows for the list,
/// ordered by name once per seeded locale.
/// </summary>
internal sealed class CurrencySnapshot : ICurrencyReader, ICurrencyCatalogue
{
    private Catalogue? _catalogue;

    private Catalogue Loaded =>
        _catalogue ?? throw new InvalidOperationException("The currency catalogue is not loaded.");

    public async Task<LoadedCatalogue> LoadAsync(
        GeoDbContext db,
        CancellationToken cancellationToken
    )
    {
        List<CurrencyRow> rows = await db.Set<CurrencyRow>()
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        List<CurrencyNameRow> names = await db.Set<CurrencyNameRow>()
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        FrozenDictionary<string, Labels> labels = Labels.ByKey(
            names,
            static name => name.CurrencyCode,
            static name => name.Locale,
            static name => name.Name
        );
        Currency[] currencies =
        [
            .. rows.Select(row => new Currency(
                CurrencyCode.Create(row.CurrencyCode).Value,
                row.NumericCode,
                row.MinorUnit is { } digits ? MinorUnit.Create(digits).Value : null,
                row.Status,
                Labels.Of(labels, "currency", row.CurrencyCode)
            )),
        ];
        _catalogue = new Catalogue(
            currencies.ToFrozenDictionary(
                static currency => currency.Code.Value,
                StringComparer.Ordinal
            ),
            Labels.OrderedByLocale(
                [.. currencies.Where(static currency => currency.Status == ReferenceStatus.Active)],
                static currency => currency.Names,
                static currency => currency.Code.Value,
                static (currency, name) =>
                    new CurrencyDto(currency.Code, currency.NumericCode, currency.MinorUnit, name)
            )
        );
        return new LoadedCatalogue(
            rows.Count,
            [
                .. rows.Select(static row =>
                        $"currency|{row.CurrencyCode}|{row.NumericCode}|{row.MinorUnit}|{row.Status}"
                    )
                    .Concat(
                        names.Select(static name =>
                            $"currency_name|{name.CurrencyCode}|{name.Locale}|{name.Name}"
                        )
                    )
                    .Order(StringComparer.Ordinal),
            ]
        );
    }

    public Localized<IReadOnlyList<CurrencyDto>> List(Locale locale) =>
        Labels.Pick(Loaded.Active, locale);

    public ValueTask<ErrorOr<MinorUnit>> MinorUnitOf(
        CurrencyCode currency,
        CancellationToken cancellationToken
    ) =>
        ValueTask.FromResult<ErrorOr<MinorUnit>>(
            Loaded.All.TryGetValue(currency.Value, out Currency? found)
                ? found.MinorUnit is { } digits
                    ? digits
                    : CurrencyErrors.MinorUnitUndefined
                : CurrencyErrors.NotFound
        );

    private sealed record Currency(
        CurrencyCode Code,
        string? NumericCode,
        MinorUnit? MinorUnit,
        ReferenceStatus Status,
        Labels Names
    );

    private sealed record Catalogue(
        FrozenDictionary<string, Currency> All,
        FrozenDictionary<string, IReadOnlyList<CurrencyDto>> Active
    );
}
