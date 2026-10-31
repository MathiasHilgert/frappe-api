using Frappe.Geo.Contracts;
using Frappe.Geo.Contracts.ValueObjects;
using Frappe.Geo.Infrastructure.Catalogues;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;

namespace Frappe.Geo.IntegrationTests;

// The geo Contracts ports other modules call, resolved from the running host.
public sealed class CatalogueTests(GeoFixture geo)
{
    [Theory]
    [InlineData("ARS", 2)]
    [InlineData("CLP", 0)]
    [InlineData("BGN", 2)]
    public async Task A_currency_has_its_minor_unit_retired_ones_included(string code, int expected)
    {
        ICurrencyCatalogue currencies = geo.Host.Services.GetRequiredService<ICurrencyCatalogue>();

        ErrorOr<MinorUnit> minorUnit = await currencies.MinorUnitOf(
            CurrencyCode.Create(code).Value,
            TestContext.Current.CancellationToken
        );

        minorUnit.Value.Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData("XAU", "geo.currency.minor_unit_undefined")]
    [InlineData("ZZZ", "geo.currency.not_found")]
    public async Task A_currency_without_a_minor_unit_or_outside_the_catalogue_has_none(
        string code,
        string error
    )
    {
        ICurrencyCatalogue currencies = geo.Host.Services.GetRequiredService<ICurrencyCatalogue>();

        ErrorOr<MinorUnit> minorUnit = await currencies.MinorUnitOf(
            CurrencyCode.Create(code).Value,
            TestContext.Current.CancellationToken
        );

        minorUnit.FirstError.Code.ShouldBe(error);
    }

    [Fact]
    public async Task A_country_resolves_with_its_name_along_the_locale_fallback_chain()
    {
        ICountryCatalogue countries = geo.Host.Services.GetRequiredService<ICountryCatalogue>();

        ErrorOr<GeoCountry> country = await countries.GetCountry(
            CountryCode.Create("DE").Value,
            Locale.Create("fr").Value,
            TestContext.Current.CancellationToken
        );

        country.Value.ShouldBe(
            new GeoCountry(
                CountryCode.Create("DE").Value,
                "Germany",
                CurrencyCode.Create("EUR").Value,
                ReferenceStatus.Active
            )
        );
    }

    [Fact]
    public async Task A_country_outside_the_catalogue_is_not_found()
    {
        ICountryCatalogue countries = geo.Host.Services.GetRequiredService<ICountryCatalogue>();

        ErrorOr<GeoCountry> country = await countries.GetCountry(
            CountryCode.Create("ZZ").Value,
            Locale.English,
            TestContext.Current.CancellationToken
        );

        country.FirstError.Code.ShouldBe("geo.country.not_found");
    }

    [Fact]
    public async Task A_country_has_its_current_tender_as_default_currency()
    {
        ICountryCatalogue countries = geo.Host.Services.GetRequiredService<ICountryCatalogue>();

        ErrorOr<CurrencyCode> currency = await countries.DefaultCurrencyOf(
            CountryCode.Create("AR").Value,
            TestContext.Current.CancellationToken
        );

        currency.Value.Value.ShouldBe("ARS");
    }

    [Fact]
    public async Task A_country_without_a_current_tender_has_no_default_currency()
    {
        ICountryCatalogue countries = geo.Host.Services.GetRequiredService<ICountryCatalogue>();

        ErrorOr<CurrencyCode> currency = await countries.DefaultCurrencyOf(
            CountryCode.Create("AQ").Value,
            TestContext.Current.CancellationToken
        );

        currency.FirstError.Code.ShouldBe("geo.country.default_currency_undefined");
    }

    [Fact]
    public async Task A_city_resolves_with_its_province_country_and_zone()
    {
        await using AsyncServiceScope scope = geo.Host.Services.CreateAsyncScope();
        ICityCatalogue cities = scope.ServiceProvider.GetRequiredService<ICityCatalogue>();

        ErrorOr<GeoCity> city = await cities.GetCityAsync(
            CityIds.From("14042170"),
            TestContext.Current.CancellationToken
        );

        city.Value.ShouldBe(
            new GeoCity(
                CityIds.From("14042170"),
                "Villa María",
                CountryCode.Create("AR").Value,
                SubdivisionCode.Create("AR-X").Value,
                TimeZoneId.Create("America/Argentina/Cordoba").Value,
                ReferenceStatus.Active
            )
        );
    }

    [Fact]
    public async Task A_city_outside_the_catalogue_is_not_found()
    {
        await using AsyncServiceScope scope = geo.Host.Services.CreateAsyncScope();
        ICityCatalogue cities = scope.ServiceProvider.GetRequiredService<ICityCatalogue>();

        ErrorOr<GeoCity> city = await cities.GetCityAsync(
            CityId.NewId(),
            TestContext.Current.CancellationToken
        );

        city.FirstError.Code.ShouldBe("geo.city.not_found");
    }

    [Theory]
    [InlineData("es-AR")]
    [InlineData("es")]
    [InlineData("ca-ES-valencia")]
    public async Task A_listed_locale_exists(string tag)
    {
        ILocaleCatalogue locales = geo.Host.Services.GetRequiredService<ILocaleCatalogue>();

        ErrorOr<Success> exists = await locales.Exists(
            Locale.Create(tag).Value,
            TestContext.Current.CancellationToken
        );

        exists.IsError.ShouldBeFalse();
    }

    // Only the exact canonical tag counts: es is listed, es-XK is not, and it does not fall back to es.
    [Theory]
    [InlineData("es-XK")]
    [InlineData("tlh")]
    public async Task A_locale_that_is_not_listed_exactly_is_not_found(string tag)
    {
        ILocaleCatalogue locales = geo.Host.Services.GetRequiredService<ILocaleCatalogue>();

        ErrorOr<Success> exists = await locales.Exists(
            Locale.Create(tag).Value,
            TestContext.Current.CancellationToken
        );

        exists.FirstError.Code.ShouldBe("geo.locale.not_found");
    }

    [Fact]
    public async Task Without_a_subdivision_a_years_holidays_are_national_only()
    {
        IHolidayCatalogue holidays = geo.Host.Services.GetRequiredService<IHolidayCatalogue>();

        ErrorOr<IReadOnlyList<GeoHoliday>> year = await holidays.HolidaysFor(
            CountryCode.Create("AR").Value,
            2026,
            subdivision: null,
            Locale.English,
            TestContext.Current.CancellationToken
        );

        year.Value.ShouldNotBeEmpty();
        year.Value.ShouldAllBe(holiday => holiday.Subdivision == null);
    }

    [Fact]
    public async Task A_subdivision_adds_only_its_own_holidays_in_the_asked_locale()
    {
        IHolidayCatalogue holidays = geo.Host.Services.GetRequiredService<IHolidayCatalogue>();
        SubdivisionCode cordoba = SubdivisionCode.Create("AR-X").Value;

        ErrorOr<IReadOnlyList<GeoHoliday>> year = await holidays.HolidaysFor(
            CountryCode.Create("AR").Value,
            2026,
            cordoba,
            Locale.Create("fr").Value,
            TestContext.Current.CancellationToken
        );

        year.Value.ShouldAllBe(holiday =>
            holiday.Subdivision == null || holiday.Subdivision == cordoba
        );

        year.Value.ShouldContain(
            new GeoHoliday(
                new LocalDate(2026, 11, 10),
                HolidayClass.Holiday,
                HolidayOrigin.Decreed,
                SubdivisionCode.Create("AR-X").Value,
                "Visit of His Holiness Pope Leo XIV"
            )
        );
        year.Value[0].Date.ShouldBe(new LocalDate(2026, 1, 1));
    }

    // The caller names the country, so one outside the catalogue is an invalid field, not a 404.
    [Fact]
    public async Task An_unknown_country_is_an_invalid_field_for_holidays()
    {
        IHolidayCatalogue holidays = geo.Host.Services.GetRequiredService<IHolidayCatalogue>();

        ErrorOr<IReadOnlyList<GeoHoliday>> result = await holidays.HolidaysFor(
            CountryCode.Create("ZZ").Value,
            2026,
            subdivision: null,
            Locale.English,
            TestContext.Current.CancellationToken
        );

        result.FirstError.Code.ShouldBe("geo.country.not_found");
        result.FirstError.Type.ShouldBe(ErrorType.Validation);
    }

    // Only Argentine subdivisions are catalogued, so the cross-country case asks Uruguay for an Argentine province.
    [Theory]
    [InlineData("AR", "AR-ZZ")]
    [InlineData("UY", "AR-B")]
    public async Task An_unknown_or_foreign_subdivision_is_an_invalid_field(
        string country,
        string subdivision
    )
    {
        IHolidayCatalogue holidays = geo.Host.Services.GetRequiredService<IHolidayCatalogue>();

        ErrorOr<IReadOnlyList<GeoHoliday>> result = await holidays.HolidaysFor(
            CountryCode.Create(country).Value,
            2026,
            SubdivisionCode.Create(subdivision).Value,
            Locale.English,
            TestContext.Current.CancellationToken
        );

        result.FirstError.Code.ShouldBe("geo.subdivision.not_found");
        result.FirstError.Type.ShouldBe(ErrorType.Validation);
    }

    [Theory]
    [InlineData("AR", 2031, "geo.holiday.year_unavailable")]
    [InlineData("ZZ", 2026, "geo.country.not_found")]
    public async Task An_unseeded_year_or_an_unknown_country_has_no_holidays(
        string country,
        int year,
        string error
    )
    {
        IHolidayCatalogue holidays = geo.Host.Services.GetRequiredService<IHolidayCatalogue>();

        ErrorOr<IReadOnlyList<GeoHoliday>> result = await holidays.HolidaysFor(
            CountryCode.Create(country).Value,
            year,
            subdivision: null,
            Locale.English,
            TestContext.Current.CancellationToken
        );

        result.FirstError.Code.ShouldBe(error);
    }
}
