using System.Net;
using System.Text.Json;
using Frappe.Geo.IntegrationTests.Hosts;
using Frappe.Geo.IntegrationTests.Persistence;
using Frappe.Geo.IntegrationTests.Sync;

namespace Frappe.Geo.IntegrationTests.BusinessDays;

/// <summary>
/// The business-day endpoints after real syncs of the territory and the 2026 national, provincial and banking (BCRA)
/// calendars. 2026: 6 November is the bank workers' day (sector rule), 9 November a national holiday, 10 November a
/// BCRA-only banking holiday and a holiday in Córdoba (AR-X), 23 March a tourist non-working day. Read-only.
/// </summary>
[ClassDataSource<GeoDatabase, ValkeyCache>(Shared = [SharedType.PerClass, SharedType.PerTestSession])]
public sealed class BusinessDayEndpointsTests(GeoDatabase database, ValkeyCache valkey)
{
    private static readonly SemaphoreSlim Once = new(1, 1);
    private static readonly HashSet<string> Synced = [];

    private async Task<GeoApi.Answer> GetAsync(string path, string? acceptLanguage = null)
    {
        await Once.WaitAsync();
        try
        {
            if (Synced.Add(database.Token))
            {
                await SourceSync.TerritoryAsync(database, new SourceServer());
                await SourceSync.HolidaysAsync(database, new SourceServer("holidays"));
                await SourceSync.ProvincialHolidaysAsync(database, SourceSync.Calendarios());
                await SourceSync.BankingHolidaysAsync(database, SourceSync.Bcra());
            }
        }
        finally
        {
            Once.Release();
        }

        return await GeoApi.GetAsync(database, valkey, path, acceptLanguage);
    }

    [Test]
    public async Task A_country_says_what_geo_covers_for_it()
    {
        var argentina = await GetAsync("/v1/geo/countries/AR");
        var uruguay = await GetAsync("/v1/geo/countries/UY");

        var capabilities = argentina.Body.GetProperty("capabilities");
        var holidays = capabilities.GetProperty("holidays").EnumerateArray()
            .ToDictionary(h => h.GetProperty("scope").GetString()!, h => h.GetProperty("years").EnumerateArray().Select(y => y.GetInt32()).ToArray());
        await Assert.That(holidays.Keys).IsEquivalentTo(["banking", "general"]);
        await Assert.That(holidays["general"]).IsEquivalentTo([2026]);
        await Assert.That(capabilities.GetProperty("businessDayPolicies").EnumerateArray().Select(p => p.GetString()!)).IsEquivalentTo(["banking", "labor", "commercial"]);
        await Assert.That(capabilities.GetProperty("timeZone").GetString()).IsEqualTo("America/Argentina/Buenos_Aires");
        await Assert.That(uruguay.Body.GetProperty("capabilities").GetProperty("holidays").GetArrayLength()).IsEqualTo(0);
        await Assert.That(uruguay.Body.GetProperty("capabilities").GetProperty("businessDayPolicies").GetArrayLength()).IsEqualTo(0);
        await Assert.That(uruguay.Body.GetProperty("capabilities").GetProperty("timeZone").ValueKind).IsEqualTo(JsonValueKind.Null);
    }
}
