using System.Net;
using Frappe.Geo.IntegrationTests.Persistence;
using Frappe.Geo.IntegrationTests.Sync;

namespace Frappe.Geo.IntegrationTests.Hosts;

/// <summary>The holiday endpoints after real syncs of the territory and the 2026 national calendar. Read-only: one database for the class.</summary>
[ClassDataSource<GeoDatabase, ValkeyCache>(Shared = [SharedType.PerClass, SharedType.PerTestSession])]
public sealed class HolidayEndpointsTests(GeoDatabase database, ValkeyCache valkey)
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
                await SourceSync.HolidaysAsync(database, new SourceServer("holidays"));
            }
        }
        finally
        {
            Once.Release();
        }

        return await GeoApi.GetAsync(database, valkey, path);
    }

    private static string? ErrorCode(GeoApi.Answer answer) => answer.Body.GetProperty("errors")[0].GetProperty("code").GetString();

    [Test]
    public async Task A_country_answers_its_national_observances_by_date_with_their_kind_and_basis()
    {
        var answer = await GetAsync("/v1/geo/countries/ar/holidays?year=2026");

        var observances = answer.Body.GetProperty("observances").EnumerateArray().ToArray();
        var guemes = observances.Single(o => o.GetProperty("date").GetString() == "2026-06-15");
        await Assert.That(answer.Status).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(answer.Body.GetProperty("jurisdiction").GetString()).IsEqualTo("AR");
        await Assert.That(observances.Length).IsEqualTo(32);
        await Assert.That(observances.Select(o => o.GetProperty("date").GetString()!)).IsOrderedBy(d => d, StringComparer.Ordinal);
        await Assert.That(guemes.GetProperty("kind").GetString()).IsEqualTo("holiday");
        await Assert.That(guemes.GetProperty("movedFrom").GetString()).IsEqualTo("2026-06-17");
        await Assert.That(guemes.GetProperty("legalBasis").GetProperty("citation").GetString()).IsEqualTo("Ley 27.399, art. 6; Decreto 614/2025");
        await Assert.That(observances.Count(o => o.GetProperty("kind").GetString() == "community_non_working_day")).IsGreaterThan(0);
    }

    [Test]
    public async Task A_year_without_a_calendar_is_not_covered_and_a_bad_year_or_place_is_refused()
    {
        var uncovered = await GetAsync("/v1/geo/countries/AR/holidays?year=2030");
        var otherCountry = await GetAsync("/v1/geo/countries/UY/holidays?year=2026");
        var badYear = await GetAsync("/v1/geo/countries/AR/holidays?year=26x");
        var unknownSubdivision = await GetAsync("/v1/geo/subdivisions/AR-Q/holidays?year=2026");

        await Assert.That((uncovered.Status, ErrorCode(uncovered))).IsEqualTo((HttpStatusCode.NotFound, (string?)"geo.holidays.not_covered"));
        await Assert.That(otherCountry.Status).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That((badYear.Status, ErrorCode(badYear))).IsEqualTo((HttpStatusCode.BadRequest, (string?)"geo.holiday_year.invalid"));
        await Assert.That(unknownSubdivision.Status).IsEqualTo(HttpStatusCode.NotFound);
    }
}
