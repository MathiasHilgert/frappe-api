using System.Text.Json;
using Alba;
using NodaTime.TimeZones;

namespace Frappe.Geo.IntegrationTests.Timezones;

public sealed class ListTimezonesTests(AppFixture app)
{
    private const string Url = "/v1/geo/timezones";

    private static readonly string _tzdbETag = $"\"{TzdbDateTimeZoneSource.Default.TzdbVersion}\"";

    [Fact]
    public async Task The_list_is_one_page_of_every_zone_ordered_by_id()
    {
        IScenarioResult result = await app.Host.Scenario(static scenario =>
        {
            scenario.Get.Url(Url);
            scenario.StatusCodeShouldBeOk();
        });

        using var body = JsonDocument.Parse(await result.ReadAsTextAsync());
        JsonElement root = body.RootElement;
        root.EnumerateObject()
            .Select(static member => member.Name)
            .ShouldBe(["items", "nextCursor"]);
        root.GetProperty("nextCursor").ValueKind.ShouldBe(JsonValueKind.Null);
        string?[] ids =
        [
            .. root.GetProperty("items")
                .EnumerateArray()
                .Select(static item => item.GetProperty("id").GetString()),
        ];
        ids.Length.ShouldBe(TzdbDateTimeZoneSource.Default.CanonicalIdMap.Count);
        ids.ShouldBeInOrder(SortDirection.Ascending, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData(
        "America/Cordoba",
        """{"id":"America/Cordoba","canonicalId":"America/Argentina/Cordoba"}"""
    )]
    [InlineData(
        "America/Argentina/Cordoba",
        """{"id":"America/Argentina/Cordoba","canonicalId":null}"""
    )]
    public async Task Each_zone_carries_its_id_and_canonical_id(string id, string json)
    {
        IScenarioResult result = await app.Host.Scenario(static scenario =>
        {
            scenario.Get.Url(Url);
            scenario.StatusCodeShouldBeOk();
        });

        using var body = JsonDocument.Parse(await result.ReadAsTextAsync());
        body.RootElement.GetProperty("items")
            .EnumerateArray()
            .Single(item =>
                string.Equals(item.GetProperty("id").GetString(), id, StringComparison.Ordinal)
            )
            .GetRawText()
            .ShouldBe(json);
    }

    [Fact]
    public async Task The_list_is_publicly_cacheable_and_tagged_with_the_tzdb_version()
    {
        IScenarioResult result = await app.Host.Scenario(static scenario =>
        {
            scenario.Get.Url(Url);
            scenario.StatusCodeShouldBeOk();
        });

        result.Context.Response.Headers.ETag.ToString().ShouldBe(_tzdbETag);
        result.Context.Response.Headers.CacheControl.ToString().ShouldBe("public, max-age=86400");
        result.Context.Response.Headers.ContainsKey("Vary").ShouldBeFalse();
    }

    [Fact]
    public async Task A_matching_If_None_Match_is_answered_304_with_no_body()
    {
        IScenarioResult result = await app.Host.Scenario(static scenario =>
        {
            scenario.Get.Url(Url);
            scenario.WithRequestHeader("If-None-Match", _tzdbETag);
            scenario.StatusCodeShouldBe(304);
        });

        (await result.ReadAsTextAsync()).ShouldBeEmpty();
        result.Context.Response.Headers.ETag.ToString().ShouldBe(_tzdbETag);
    }
}
