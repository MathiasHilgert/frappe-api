using System.Text.Json;
using Alba;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using NodaTime.Testing;

namespace Frappe.Geo.IntegrationTests;

// Without a year, the calendar is the current one in the country, and a cached copy never outlives its new year.
public sealed class DefaultHolidayYearTests(GeoFixture geo)
{
    private const string Url = "/v1/geo/countries/AR/holidays";

    // 22:00 on New Year's Eve in Buenos Aires (UTC-3), already 2027 in UTC.
    private static readonly Instant _newYearsEve = Instant.FromUtc(2027, 1, 1, 1, 0);
    private static readonly Instant _newYear = Instant.FromUtc(2027, 1, 1, 3, 0);

    [Fact]
    public async Task Late_on_new_years_eve_the_current_year_is_cached_only_until_midnight()
    {
        await using IAlbaHost host = await HostAt(new FakeClock(_newYearsEve));

        (IScenarioResult result, JsonElement page) = await GeoHttp.GetAsync(host, Url, 200);

        page.GetProperty("items")[0].GetProperty("date").GetString().ShouldBe("2026-01-01");
        result.Context.Response.Headers.CacheControl.ToString().ShouldBe("public, max-age=7200");
        result.Context.Response.Headers.ETag.ToString().ShouldEndWith("-2026\"");
    }

    [Fact]
    public async Task At_the_countrys_new_year_the_next_calendar_is_served_under_another_tag()
    {
        FakeClock clock = new(_newYearsEve);
        await using IAlbaHost host = await HostAt(clock);
        (IScenarioResult before, _) = await GeoHttp.GetAsync(host, Url, 200);

        clock.Reset(_newYear);
        (IScenarioResult after, JsonElement page) = await GeoHttp.GetAsync(host, Url, 200);

        page.GetProperty("items")[0].GetProperty("date").GetString().ShouldBe("2027-01-01");
        after.Context.Response.Headers.ETag.ToString().ShouldEndWith("-2027\"");
        string.Equals(
                after.Context.Response.Headers.ETag.ToString(),
                before.Context.Response.Headers.ETag.ToString(),
                StringComparison.Ordinal
            )
            .ShouldBeFalse();
        after.Context.Response.Headers.CacheControl.ToString().ShouldBe("public, max-age=86400");
    }

    [Fact]
    public async Task A_revalidation_late_on_new_years_eve_keeps_the_capped_max_age()
    {
        await using IAlbaHost host = await HostAt(new FakeClock(_newYearsEve));
        (IScenarioResult first, _) = await GeoHttp.GetAsync(host, Url, 200);
        string tag = first.Context.Response.Headers.ETag.ToString();

        IScenarioResult result = await host.Scenario(scenario =>
        {
            scenario.Get.Url(Url);
            scenario.WithRequestHeader("If-None-Match", tag);
            scenario.StatusCodeShouldBe(304);
        });

        result.Context.Response.Headers.ETag.ToString().ShouldBe(tag);
        result.Context.Response.Headers.CacheControl.ToString().ShouldBe("public, max-age=7200");
    }

    [Fact]
    public async Task Last_years_tag_does_not_revalidate_after_the_countrys_new_year()
    {
        FakeClock clock = new(_newYearsEve);
        await using IAlbaHost host = await HostAt(clock);
        (IScenarioResult first, _) = await GeoHttp.GetAsync(host, Url, 200);

        clock.Reset(_newYear);
        IScenarioResult result = await host.Scenario(scenario =>
        {
            scenario.Get.Url(Url);
            scenario.WithRequestHeader(
                "If-None-Match",
                first.Context.Response.Headers.ETag.ToString()
            );
            scenario.StatusCodeShouldBeOk();
        });

        result.Context.Response.Headers.ETag.ToString().ShouldEndWith("-2027\"");
    }

    private Task<IAlbaHost> HostAt(FakeClock clock) =>
        geo.StartHostAsync(builder => builder.Services.AddSingleton<IClock>(clock));
}
