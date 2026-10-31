using System.Collections.Frozen;
using System.Globalization;
using Frappe.Geo.Application;
using Frappe.Geo.Application.Holidays;
using Frappe.Geo.Contracts;
using Frappe.Geo.Contracts.ValueObjects;
using Frappe.Geo.Domain.Countries;
using Frappe.Geo.Domain.Holidays;
using Frappe.Geo.Domain.Subdivisions;
using Frappe.Geo.Infrastructure.Catalogues;
using Frappe.Geo.Infrastructure.Countries;
using Frappe.Geo.Infrastructure.Subdivisions;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Frappe.Geo.Infrastructure.Holidays;

/// <summary>
/// The holiday calendars in memory, loaded once at startup: the active holidays of each seeded country and year,
/// ordered once per seeded locale by date, national before subdivision ones, then by name.
/// </summary>
/// <param name="countries">The country catalogue, which tells an unknown country from an unseeded year.</param>
/// <param name="subdivisions">The subdivision catalogue, which rejects a subdivision outside the country.</param>
internal sealed class HolidaySnapshot(CountrySnapshot countries, SubdivisionSnapshot subdivisions)
    : IHolidayReader,
        IHolidayCatalogue
{
    private FrozenDictionary<
        string,
        FrozenDictionary<string, IReadOnlyList<HolidayDto>>
    >? _calendars;

    private FrozenDictionary<string, FrozenDictionary<string, IReadOnlyList<HolidayDto>>> Loaded =>
        _calendars ?? throw new InvalidOperationException("The holiday catalogue is not loaded.");

    public async Task<LoadedCatalogue> LoadAsync(
        GeoDbContext db,
        CancellationToken cancellationToken
    )
    {
        List<HolidayRow> rows = await db.Set<HolidayRow>()
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        List<HolidayNameRow> names = await db.Set<HolidayNameRow>()
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        FrozenDictionary<string, Labels> labels = Labels.ByKey(
            names,
            static name => name.HolidayId.ToString(),
            static name => name.Locale,
            static name => name.Name
        );
        _calendars = rows.Where(static row => row.Status == ReferenceStatus.Active)
            .Select(row => new Holiday(
                row.CountryCode,
                row.Date,
                row.Class,
                row.Origin,
                row.SubdivisionCode is { } subdivision
                    ? SubdivisionCode.Create(subdivision).Value
                    : null,
                row.HolidayKey,
                Labels.Of(labels, "holiday", row.HolidayId.ToString())
            ))
            .GroupBy(
                static holiday => CalendarKey(holiday.Country, holiday.Date.Year),
                StringComparer.Ordinal
            )
            .ToFrozenDictionary(
                static calendar => calendar.Key,
                static calendar => ByLocale([.. calendar]),
                StringComparer.Ordinal
            );
        return new LoadedCatalogue(
            rows.Count,
            [
                .. rows.Select(static row =>
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"holiday|{row.HolidayId}|{row.CountryCode}|{row.SubdivisionCode}|{row.Date:uuuu-MM-dd}|{row.HolidayKey}|{row.Class}|{row.Origin}|{row.Status}"
                        )
                    )
                    .Concat(
                        names.Select(static name =>
                            $"holiday_name|{name.HolidayId}|{name.Locale}|{name.Name}"
                        )
                    )
                    .Order(StringComparer.Ordinal),
            ]
        );
    }

    public ErrorOr<Localized<IReadOnlyList<HolidayDto>>> List(
        CountryCode country,
        int year,
        SubdivisionCode? subdivision,
        Locale locale
    )
    {
        if (
            !Loaded.TryGetValue(
                CalendarKey(country.Value, year),
                out FrozenDictionary<string, IReadOnlyList<HolidayDto>>? calendar
            )
        )
        {
            return HolidayErrors.YearUnavailable;
        }

        Localized<IReadOnlyList<HolidayDto>> all = Labels.Pick(calendar, locale);
        return all with
        {
            Value =
            [
                .. all.Value.Where(holiday =>
                    holiday.SubdivisionCode is null || holiday.SubdivisionCode == subdivision
                ),
            ],
        };
    }

    // The same checks, list and subdivision rule as the endpoint; the caller's country and subdivision are input
    // fields, so one outside the catalogue is invalid (never a 404).
    public ValueTask<ErrorOr<IReadOnlyList<GeoHoliday>>> HolidaysFor(
        CountryCode country,
        int year,
        SubdivisionCode? subdivision,
        Locale locale,
        CancellationToken cancellationToken
    ) =>
        ValueTask.FromResult(
            countries.Find(country, Locale.English) is null
                ? UnknownFilter.Field(CountryErrors.NotFound)
            : subdivision is { } filter && !subdivisions.BelongsTo(filter, country)
                ? UnknownFilter.Field(SubdivisionErrors.NotFound)
            : List(country, year, subdivision, locale)
                .Then(static list =>
                    (IReadOnlyList<GeoHoliday>)
                        [
                            .. list.Value.Select(static holiday => new GeoHoliday(
                                holiday.Date,
                                holiday.Class,
                                holiday.Origin,
                                holiday.SubdivisionCode,
                                holiday.Name
                            )),
                        ]
                )
        );

    private static string CalendarKey(string country, int year) =>
        string.Create(CultureInfo.InvariantCulture, $"{country}|{year}");

    // One list per seeded locale, so a request filters a ready list instead of sorting.
    private static FrozenDictionary<string, IReadOnlyList<HolidayDto>> ByLocale(
        Holiday[] holidays
    ) =>
        holidays
            .SelectMany(static holiday => holiday.Names.Locales)
            .Distinct(StringComparer.Ordinal)
            .ToFrozenDictionary(
                static tag => tag,
                tag =>
                {
                    Locale locale = Locale.Create(tag).Value;
                    var byName = StringComparer.Create(new CultureInfo(tag), ignoreCase: true);
                    return (IReadOnlyList<HolidayDto>)
                        [
                            .. holidays
                                .Select(holiday => holiday.ToDto(holiday.Names.Resolve(locale)))
                                .OrderBy(static holiday => holiday.Date)
                                .ThenBy(static holiday => holiday.SubdivisionCode is not null)
                                .ThenBy(static holiday => holiday.Name, byName)
                                .ThenBy(
                                    static holiday => holiday.SubdivisionCode?.Value,
                                    StringComparer.Ordinal
                                ),
                        ];
                },
                StringComparer.Ordinal
            );

    private sealed record Holiday(
        string Country,
        LocalDate Date,
        HolidayClass Class,
        HolidayOrigin Origin,
        SubdivisionCode? Subdivision,
        string Key,
        Labels Names
    )
    {
        public HolidayDto ToDto(string name) => new(Date, Class, Origin, Subdivision, name);
    }
}
