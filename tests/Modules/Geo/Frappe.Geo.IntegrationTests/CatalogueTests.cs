using Frappe.Geo.Contracts;
using Frappe.Geo.Contracts.ValueObjects;
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
    public async Task A_years_holidays_hold_every_subdivision_in_the_asked_locale()
    {
        IHolidayCatalogue holidays = geo.Host.Services.GetRequiredService<IHolidayCatalogue>();

        ErrorOr<IReadOnlyList<GeoHoliday>> year = await holidays.HolidaysFor(
            CountryCode.Create("AR").Value,
            2026,
            Locale.Create("fr").Value,
            TestContext.Current.CancellationToken
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
            Locale.English,
            TestContext.Current.CancellationToken
        );

        result.FirstError.Code.ShouldBe(error);
    }
}
