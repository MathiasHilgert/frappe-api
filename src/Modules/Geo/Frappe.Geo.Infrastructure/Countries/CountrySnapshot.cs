using System.Collections.Frozen;
using Frappe.Geo.Application;
using Frappe.Geo.Application.Countries;
using Frappe.Geo.Contracts;
using Frappe.Geo.Contracts.ValueObjects;
using Frappe.Geo.Domain.Countries;
using Frappe.Geo.Infrastructure.Catalogues;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Geo.Infrastructure.Countries;

/// <summary>
/// The country catalogue in memory, loaded once at startup: every row for the ports and the detail, the active rows
/// for the list, ordered by name once per seeded locale.
/// </summary>
internal sealed class CountrySnapshot : ICountryReader, ICountryCatalogue
{
    private Catalogue? _catalogue;

    private Catalogue Loaded =>
        _catalogue ?? throw new InvalidOperationException("The country catalogue is not loaded.");

    public async Task<LoadedCatalogue> LoadAsync(
        GeoDbContext db,
        CancellationToken cancellationToken
    )
    {
        List<CountryRow> rows = await db.Set<CountryRow>()
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        List<CountryNameRow> names = await db.Set<CountryNameRow>()
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        FrozenDictionary<string, Labels> labels = Labels.ByKey(
            names,
            static name => name.CountryCode,
            static name => name.Locale,
            static name => name.Name
        );
        Country[] countries =
        [
            .. rows.Select(row => new Country(
                CountryCode.Create(row.CountryCode).Value,
                row.Alpha3,
                row.NumericCode,
                row.DefaultCurrencyCode is { } currency
                    ? CurrencyCode.Create(currency).Value
                    : null,
                row.CallingCode,
                row.Status,
                labels[row.CountryCode]
            )),
        ];
        _catalogue = new Catalogue(
            countries.ToFrozenDictionary(
                static country => country.Code.Value,
                StringComparer.Ordinal
            ),
            Labels.OrderedByLocale(
                [.. countries.Where(static country => country.Status == ReferenceStatus.Active)],
                static country => country.Names,
                static country => country.Code.Value,
                static (country, name) => country.ToDto(name)
            )
        );
        return new LoadedCatalogue(
            rows.Count,
            [
                .. rows.Select(static row =>
                        $"country|{row.CountryCode}|{row.Alpha3}|{row.NumericCode}|{row.DefaultCurrencyCode}|{row.CallingCode}|{row.Status}"
                    )
                    .Concat(
                        names.Select(static name =>
                            $"country_name|{name.CountryCode}|{name.Locale}|{name.Name}"
                        )
                    )
                    .Order(StringComparer.Ordinal),
            ]
        );
    }

    public Localized<IReadOnlyList<CountryDto>> List(Locale locale) =>
        Labels.Pick(Loaded.Active, locale);

    public Localized<CountryDto>? Find(CountryCode country, Locale locale)
    {
        if (!Loaded.All.TryGetValue(country.Value, out Country? found))
        {
            return null;
        }

        Localized<string> name = found.Names.Localize(locale);
        return new Localized<CountryDto>(found.ToDto(name.Value), name.Locale);
    }

    public ValueTask<ErrorOr<GeoCountry>> GetCountry(
        CountryCode country,
        Locale locale,
        CancellationToken cancellationToken
    ) =>
        ValueTask.FromResult<ErrorOr<GeoCountry>>(
            Loaded.All.TryGetValue(country.Value, out Country? found)
                ? new GeoCountry(
                    found.Code,
                    found.Names.Resolve(locale),
                    found.DefaultCurrency,
                    found.Status
                )
                : CountryErrors.NotFound
        );

    public ValueTask<ErrorOr<CurrencyCode>> DefaultCurrencyOf(
        CountryCode country,
        CancellationToken cancellationToken
    ) =>
        ValueTask.FromResult<ErrorOr<CurrencyCode>>(
            Loaded.All.TryGetValue(country.Value, out Country? found)
                ? found.DefaultCurrency is { } currency
                    ? currency
                    : CountryErrors.DefaultCurrencyUndefined
                : CountryErrors.NotFound
        );

    private sealed record Country(
        CountryCode Code,
        string Alpha3,
        string NumericCode,
        CurrencyCode? DefaultCurrency,
        string? CallingCode,
        ReferenceStatus Status,
        Labels Names
    )
    {
        public CountryDto ToDto(string name) =>
            new(Code, Alpha3, NumericCode, name, DefaultCurrency, CallingCode);
    }

    private sealed record Catalogue(
        FrozenDictionary<string, Country> All,
        FrozenDictionary<string, IReadOnlyList<CountryDto>> Active
    );
}
