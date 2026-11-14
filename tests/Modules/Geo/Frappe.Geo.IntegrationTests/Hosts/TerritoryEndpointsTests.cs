using System.Net;
using Frappe.Geo.IntegrationTests.Persistence;
using Frappe.Geo.IntegrationTests.Sync;

namespace Frappe.Geo.IntegrationTests.Hosts;

/// <summary>The territory endpoints after a real sync of the Georef fixtures. Read-only: one synced database for the class.</summary>
[ClassDataSource<GeoDatabase, ValkeyCache>(Shared = [SharedType.PerClass, SharedType.PerTestSession])]
public sealed class TerritoryEndpointsTests(GeoDatabase database, ValkeyCache valkey)
{
    private static readonly SemaphoreSlim Once = new(1, 1);
    private static readonly HashSet<string> Synced = [];

    private async Task<GeoApi.Answer> GetAsync(string path)
    {
        await Once.WaitAsync();
        try
        {
            if (Synced.Add(database.Token))
            {
                await SourceSync.TerritoryAsync(database, new SourceServer());
            }
        }
        finally
        {
            Once.Release();
        }

        return await GeoApi.GetAsync(database, valkey, path, "es");
    }

    [Test]
    public async Task A_country_lists_its_subdivisions_with_their_category_and_aliases()
    {
        var answer = await GetAsync("/v1/geo/countries/ar/subdivisions");

        var buenosAires = answer.Body.GetProperty("subdivisions").EnumerateArray().Single(s => s.GetProperty("code").GetString() == "AR-C");
        await Assert.That(answer.Status).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(buenosAires.GetProperty("category").GetString()).IsEqualTo("Ciudad Autónoma");
        await Assert.That(buenosAires.GetProperty("aliases").EnumerateArray().Select(a => a.GetString()!)).Contains("CABA");
        await Assert.That(answer.ContentLanguage).IsNull();
    }

    [Test]
    public async Task A_country_without_territory_is_not_covered_rather_than_empty_and_an_unknown_one_404()
    {
        var uruguay = await GetAsync("/v1/geo/countries/UY/subdivisions");
        var unknown = await GetAsync("/v1/geo/countries/QX/subdivisions");

        await Assert.That(uruguay.Status).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(uruguay.Body.GetProperty("errors")[0].GetProperty("code").GetString()).IsEqualTo("geo.territory.not_covered");
        await Assert.That(uruguay.Body.GetProperty("errors")[0].GetProperty("message").GetString()).Contains("UY");
        await Assert.That(unknown.Body.GetProperty("errors")[0].GetProperty("code").GetString()).IsEqualTo("geo.country.not_found");
    }

    [Test]
    public async Task Areas_answer_by_level_and_a_local_government_by_its_public_id()
    {
        var level3 = await GetAsync("/v1/geo/subdivisions/AR-X/areas?level=3");
        var city = await GetAsync("/v1/geo/areas/AR:140077");

        var levels = level3.Body.GetProperty("areas").EnumerateArray().Select(a => a.GetProperty("level").GetInt32()).Distinct();
        await Assert.That(levels).IsEquivalentTo([3]);
        await Assert.That(city.Body.GetProperty("isLocalGovernment").GetBoolean()).IsTrue();
        await Assert.That(city.Body.GetProperty("name").GetString()).IsEqualTo("Córdoba");
    }

    [Test]
    public async Task A_misspelled_search_finds_the_locality_and_links_its_areas()
    {
        var answer = await GetAsync("/v1/geo/subdivisions/AR-X/localities?q=cordova");

        var first = answer.Body.GetProperty("localities")[0];
        await Assert.That(first.GetProperty("id").GetString()).IsEqualTo("AR:14014010");
        await Assert.That(first.GetProperty("areaId").GetString()).IsEqualTo("AR:14014");
        await Assert.That(first.GetProperty("localGovernmentId").GetString()).IsEqualTo("AR:140077");
    }

    [Test]
    public async Task Pages_follow_the_cursor_and_a_cursor_of_another_search_is_refused()
    {
        var first = await GetAsync("/v1/geo/subdivisions/AR-X/localities?limit=1");
        var cursor = first.Body.GetProperty("nextCursor").GetString();
        var second = await GetAsync($"/v1/geo/subdivisions/AR-X/localities?limit=1&cursor={cursor}");
        var mixed = await GetAsync($"/v1/geo/subdivisions/AR-X/localities?limit=1&q=paz&cursor={cursor}");

        await Assert.That(second.Body.GetProperty("localities")[0].GetProperty("id").GetString()).IsNotEqualTo(first.Body.GetProperty("localities")[0].GetProperty("id").GetString());
        await Assert.That(second.Body.TryGetProperty("nextCursor", out var next) && next.ValueKind != System.Text.Json.JsonValueKind.Null).IsFalse();
        await Assert.That(mixed.Body.GetProperty("errors")[0].GetProperty("code").GetString()).IsEqualTo("geo.page_cursor.invalid");
    }

    [Test]
    [Arguments("/v1/geo/subdivisions/ARX", 400, "geo.subdivision_code.invalid_format")]
    [Arguments("/v1/geo/subdivisions/AR-Z", 404, "geo.subdivision.not_found")]
    [Arguments("/v1/geo/subdivisions/AR-Z/areas", 404, "geo.subdivision.not_found")]
    [Arguments("/v1/geo/subdivisions/AR-Z/localities", 404, "geo.subdivision.not_found")]
    [Arguments("/v1/geo/subdivisions/US-CA", 404, "geo.territory.not_covered")]
    [Arguments("/v1/geo/subdivisions/US-CA/areas", 404, "geo.territory.not_covered")]
    [Arguments("/v1/geo/subdivisions/US-CA/localities", 404, "geo.territory.not_covered")]
    [Arguments("/v1/geo/subdivisions/QX-AB", 404, "geo.subdivision.not_found")]
    [Arguments("/v1/geo/subdivisions/AR-X/areas?level=1", 400, "geo.area_level.out_of_range")]
    [Arguments("/v1/geo/subdivisions/AR-X/areas?level=x", 400, "geo.area_level.invalid")]
    [Arguments("/v1/geo/subdivisions/AR-X/localities?limit=0", 400, "geo.page_limit.out_of_range")]
    [Arguments("/v1/geo/subdivisions/AR-X/localities?limit=ten", 400, "geo.page_limit.invalid")]
    [Arguments("/v1/geo/areas/AR:000", 404, "geo.area.not_found")]
    [Arguments("/v1/geo/localities/garbage", 404, "geo.locality.not_found")]
    public async Task Errors_are_problem_details_with_a_stable_code(string path, int status, string code)
    {
        var answer = await GetAsync(path);

        await Assert.That((int)answer.Status).IsEqualTo(status);
        await Assert.That(answer.Body.GetProperty("errors")[0].GetProperty("code").GetString()).IsEqualTo(code);
    }
}
