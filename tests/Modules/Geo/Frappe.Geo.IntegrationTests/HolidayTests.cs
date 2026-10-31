using System.Text.Json;
using Alba;
using Frappe.TestUtilities;

namespace Frappe.Geo.IntegrationTests;

public sealed class HolidayTests(GeoFixture geo)
{
    private const string Url = "/v1/geo/countries/AR/holidays";

    [Fact]
    public async Task The_2026_calendar_matches_its_snapshot()
    {
        (IScenarioResult result, _) = await GeoHttp.GetAsync(
            geo.Host,
            $"{Url}?year=2026&locale=es",
            200
        );

        Snapshot.ShouldMatch(await result.ReadAsTextAsync(), "holidays-ar-2026-es");
    }

    [Fact]
    public async Task Two_holidays_on_one_day_are_two_rows_each_with_its_own_class()
    {
        (_, JsonElement page) = await GeoHttp.GetAsync(geo.Host, $"{Url}?year=2026&locale=es", 200);

        JsonElement[] onApril2 = OnDate(page, "2026-04-02");
        onApril2.Length.ShouldBe(2);
        Fields(onApril2[0])
            .ShouldBe(
                ("holiday", "fixed", "Día del Veterano y de los Caidos en la Guerra de Malvinas")
            );
        Fields(onApril2[1]).ShouldBe(("non_working_day", "fixed", "Jueves Santo"));
        Fields(OnDate(page, "2026-05-25").Single())
            .ShouldBe(("holiday", "fixed", "Día de la Revolución de Mayo"));
    }

    [Theory]
    [InlineData("2026-08-17", "movable")]
    [InlineData("2026-12-07", "bridge")]
    [InlineData("2026-11-09", "decreed")]
    public async Task A_holiday_carries_how_it_got_its_date(string date, string origin)
    {
        (_, JsonElement page) = await GeoHttp.GetAsync(geo.Host, $"{Url}?year=2026", 200);

        OnDate(page, date).Single().GetProperty("origin").GetString().ShouldBe(origin);
    }

    [Fact]
    public async Task Without_a_subdivision_only_national_holidays_are_listed()
    {
        (_, JsonElement page) = await GeoHttp.GetAsync(geo.Host, $"{Url}?year=2026", 200);

        page.GetProperty("items")
            .EnumerateArray()
            .ShouldAllBe(item =>
                item.GetProperty("subdivisionCode").ValueKind == JsonValueKind.Null
            );
        page.GetProperty("nextCursor").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_subdivision_adds_only_its_own_holidays()
    {
        (_, JsonElement national) = await GeoHttp.GetAsync(geo.Host, $"{Url}?year=2026", 200);
        (_, JsonElement cordoba) = await GeoHttp.GetAsync(
            geo.Host,
            $"{Url}?year=2026&subdivisionCode=ar-x",
            200
        );

        JsonElement[] provincial =
        [
            .. cordoba
                .GetProperty("items")
                .EnumerateArray()
                .Where(static item =>
                    item.GetProperty("subdivisionCode").ValueKind != JsonValueKind.Null
                ),
        ];
        provincial.ShouldNotBeEmpty();
        provincial.ShouldAllBe(item => item.GetProperty("subdivisionCode").GetString() == "AR-X");
        provincial.ShouldContain(item => item.GetProperty("date").GetString() == "2026-11-10");
        cordoba
            .GetProperty("items")
            .GetArrayLength()
            .ShouldBe(national.GetProperty("items").GetArrayLength() + provincial.Length);
    }

    [Fact]
    public async Task On_one_day_national_holidays_come_before_subdivision_ones()
    {
        (_, JsonElement page) = await GeoHttp.GetAsync(
            geo.Host,
            $"{Url}?year=2026&subdivisionCode=AR-Z",
            200
        );

        JsonElement[] onDecember7 = OnDate(page, "2026-12-07");
        onDecember7.Length.ShouldBe(2);
        onDecember7[0].GetProperty("subdivisionCode").ValueKind.ShouldBe(JsonValueKind.Null);
        onDecember7[1].GetProperty("subdivisionCode").GetString().ShouldBe("AR-Z");
    }

    [Theory]
    [InlineData("es", "Día del Veterano y de los Caidos en la Guerra de Malvinas")]
    [InlineData("en", "Maundy Thursday")]
    public async Task On_one_day_and_level_holidays_are_ordered_by_name_in_the_served_locale(
        string locale,
        string first
    )
    {
        (_, JsonElement page) = await GeoHttp.GetAsync(
            geo.Host,
            $"{Url}?year=2026&locale={locale}",
            200
        );

        OnDate(page, "2026-04-02")[0].GetProperty("name").GetString().ShouldBe(first);
    }

    [Fact]
    public async Task Portuguese_names_are_served_with_their_content_language()
    {
        (IScenarioResult result, JsonElement page) = await GeoHttp.GetAsync(
            geo.Host,
            $"{Url}?year=2027&locale=pt-BR",
            200
        );

        OnDate(page, "2027-05-01")
            .Single()
            .GetProperty("name")
            .GetString()
            .ShouldBe("Dia do Trabalho");
        result.Context.Response.Headers.ContentLanguage.ToString().ShouldBe("pt");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("10000")]
    [InlineData("2026.5")]
    [InlineData("-1")]
    public async Task A_year_outside_1_to_9999_is_rejected_at_its_parameter(string year)
    {
        (IScenarioResult result, JsonElement problem) = await GeoHttp.GetAsync(
            geo.Host,
            $"{Url}?year={year}",
            422
        );

        JsonElement error = GeoHttp.FirstError(problem);
        error.GetProperty("code").GetString().ShouldBe("geo.holiday.year_invalid");
        error.GetProperty("pointer").GetString().ShouldBe("/year");
        result.Context.Response.Headers.CacheControl.ToString().ShouldBe("no-store");
    }

    [Fact]
    public async Task Every_malformed_parameter_is_reported_at_once()
    {
        (_, JsonElement problem) = await GeoHttp.GetAsync(
            geo.Host,
            $"{Url}?year=0&subdivisionCode=X&locale=e",
            422
        );

        problem
            .GetProperty("errors")
            .EnumerateArray()
            .Select(static error => error.GetProperty("pointer").GetString())
            .ShouldBe(["/locale", "/year", "/subdivisionCode"]);
    }

    [Theory]
    [InlineData("BR-SP")]
    [InlineData("AR-ZZ")]
    public async Task A_subdivision_outside_the_countrys_catalogue_is_rejected_at_its_parameter(
        string subdivision
    )
    {
        (IScenarioResult result, JsonElement problem) = await GeoHttp.GetAsync(
            geo.Host,
            $"{Url}?year=2026&subdivisionCode={subdivision}",
            422
        );

        JsonElement error = GeoHttp.FirstError(problem);
        error.GetProperty("code").GetString().ShouldBe("geo.subdivision.not_found");
        error.GetProperty("pointer").GetString().ShouldBe("/subdivisionCode");
        result.Context.Response.Headers.CacheControl.ToString().ShouldBe("no-store");
    }

    [Fact]
    public async Task An_unseeded_year_is_unavailable_rather_than_empty_and_never_stored()
    {
        (IScenarioResult result, JsonElement problem) = await GeoHttp.GetAsync(
            geo.Host,
            $"{Url}?year=2031",
            404
        );

        GeoHttp
            .FirstError(problem)
            .GetProperty("code")
            .GetString()
            .ShouldBe("geo.holiday.year_unavailable");
        result.Context.Response.Headers.CacheControl.ToString().ShouldBe("no-store");
    }

    [Theory]
    [InlineData("ZZ")]
    [InlineData("ARG")]
    public async Task An_unknown_or_malformed_country_is_not_found(string code)
    {
        (_, JsonElement problem) = await GeoHttp.GetAsync(
            geo.Host,
            $"/v1/geo/countries/{code}/holidays",
            404
        );

        GeoHttp
            .FirstError(problem)
            .GetProperty("code")
            .GetString()
            .ShouldBe("geo.country.not_found");
    }

    [Fact]
    public async Task A_country_without_a_seeded_calendar_has_no_year_available()
    {
        (_, JsonElement problem) = await GeoHttp.GetAsync(
            geo.Host,
            "/v1/geo/countries/UY/holidays?year=2026",
            404
        );

        GeoHttp
            .FirstError(problem)
            .GetProperty("code")
            .GetString()
            .ShouldBe("geo.holiday.year_unavailable");
    }

    [Fact]
    public async Task An_explicit_year_carries_the_dataset_tag_and_a_day_of_caching()
    {
        (IScenarioResult catalogue, _) = await GeoHttp.GetAsync(
            geo.Host,
            "/v1/geo/time-zones",
            200
        );
        (IScenarioResult result, _) = await GeoHttp.GetAsync(geo.Host, $"{Url}?year=2026", 200);

        result
            .Context.Response.Headers.ETag.ToString()
            .ShouldBe(catalogue.Context.Response.Headers.ETag.ToString());
        result.Context.Response.Headers.CacheControl.ToString().ShouldBe("public, max-age=86400");
    }

    private static JsonElement[] OnDate(JsonElement page, string date) =>
        [
            .. page.GetProperty("items")
                .EnumerateArray()
                .Where(item =>
                    string.Equals(
                        item.GetProperty("date").GetString(),
                        date,
                        StringComparison.Ordinal
                    )
                ),
        ];

    private static (string?, string?, string?) Fields(JsonElement holiday) =>
        (
            holiday.GetProperty("class").GetString(),
            holiday.GetProperty("origin").GetString(),
            holiday.GetProperty("name").GetString()
        );
}
