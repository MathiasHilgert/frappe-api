using System.Text.Json;

namespace Frappe.Geo.IntegrationTests;

public sealed class TimeZoneTests(GeoFixture geo)
{
    [Fact]
    public async Task The_zones_of_a_country_are_its_zone_tab_zones()
    {
        (_, JsonElement page) = await GeoHttp.GetAsync(
            geo.Host,
            "/v1/geo/time-zones?countryCode=ar",
            200
        );

        string[] zones = GeoHttp.Codes(page, "timeZoneId");
        zones.Contains("America/Argentina/Cordoba", StringComparer.Ordinal).ShouldBeTrue();
        zones.ShouldAllBe(static zone =>
            zone.StartsWith("America/Argentina/", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task A_zone_keeps_its_tzdb_id_even_when_tzdb_links_it_to_another_country()
    {
        (_, JsonElement page) = await GeoHttp.GetAsync(
            geo.Host,
            "/v1/geo/time-zones?countryCode=NO",
            200
        );

        GeoHttp.Codes(page, "timeZoneId").ShouldBe(["Europe/Oslo"]);
    }

    [Fact]
    public async Task Every_zone_is_listed_by_its_id()
    {
        (_, JsonElement page) = await GeoHttp.GetAsync(geo.Host, "/v1/geo/time-zones", 200);

        string[] zones = GeoHttp.Codes(page, "timeZoneId");
        zones.ShouldBe(zones.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Utc_is_listed_without_a_country()
    {
        (_, JsonElement page) = await GeoHttp.GetAsync(geo.Host, "/v1/geo/time-zones", 200);

        JsonElement utc = GeoHttp.Item(page, "timeZoneId", "Etc/UTC");
        utc.GetProperty("countryCode").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Links_outside_zone_tab_are_not_listed()
    {
        (_, JsonElement page) = await GeoHttp.GetAsync(geo.Host, "/v1/geo/time-zones", 200);

        GeoHttp
            .Codes(page, "timeZoneId")
            .Contains("US/Eastern", StringComparer.Ordinal)
            .ShouldBeFalse();
    }

    [Theory]
    [InlineData("ZZ", "geo.country.not_found")]
    [InlineData("ARG", "geo.country_code.invalid")]
    public async Task A_country_filter_outside_the_catalogue_is_rejected_at_its_parameter(
        string countryCode,
        string errorCode
    )
    {
        (_, JsonElement problem) = await GeoHttp.GetAsync(
            geo.Host,
            $"/v1/geo/time-zones?countryCode={countryCode}",
            422
        );

        JsonElement error = GeoHttp.FirstError(problem);
        error.GetProperty("code").GetString().ShouldBe(errorCode);
        error.GetProperty("pointer").GetString().ShouldBe("/countryCode");
    }
}
