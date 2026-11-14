using System.Net;
using Frappe.Geo.Countries.Argentina;
using Frappe.Geo.Infrastructure;
using Frappe.Geo.Infrastructure.Sync;
using Frappe.Geo.IntegrationTests.Hosts;
using Frappe.Geo.IntegrationTests.Persistence;
using Frappe.Geo.IntegrationTests.Sync;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using static Frappe.Geo.IntegrationTests.Admin.AdminRequests;

namespace Frappe.Geo.IntegrationTests.Admin;

/// <summary>holiday-syncs (S12): the holiday sources, their run history, and a run requested by hand that the Worker starts.</summary>
[ClassDataSource<ValkeyCache>(Shared = SharedType.PerTestSession)]
public sealed class HolidaySyncAdminTests(ValkeyCache valkey)
{
    private const string Source = "ar-official-holidays";

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task The_holiday_sources_are_listed_with_their_runs_newest_first(GeoDatabase database)
    {
        var government = new SourceServer("holidays");
        await SourceSync.TerritoryAsync(database, new SourceServer());
        await SourceSync.HolidaysAsync(database, government);
        await SourceSync.HolidaysAsync(database, government);

        var sources = await SendAsync(database, valkey, HttpMethod.Get, "/v1/geo/holiday-syncs");
        var runs = await SendAsync(database, valkey, HttpMethod.Get, $"/v1/geo/holiday-syncs/{Source}/runs");

        var listed = sources.Body.GetProperty("sources").EnumerateArray().ToArray();
        var outcomes = runs.Body.GetProperty("runs").EnumerateArray().Select(r => r.GetProperty("outcome").GetString()!).ToArray();
        await Assert.That(listed.Select(s => s.GetProperty("sourceId").GetString()!)).IsEquivalentTo([Source]);
        await Assert.That(listed[0].GetProperty("lastOutcome").GetString()).IsEqualTo("unchanged");
        await Assert.That(outcomes).IsEquivalentTo(["unchanged", "applied"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task A_requested_run_is_accepted_and_the_Worker_triggers_that_source_job_once(GeoDatabase database)
    {
        await SourceSync.HolidaysAsync(database, new SourceServer("holidays"));

        var requested = await SendAsync(database, valkey, HttpMethod.Post, $"/v1/geo/holiday-syncs/{Source}/runs");
        var again = await SendAsync(database, valkey, HttpMethod.Post, $"/v1/geo/holiday-syncs/{Source}/runs");
        await using var db = database.NewContext();
        var dispatch = new SyncRequestDispatch(db, new Dictionary<string, string>(StringComparer.Ordinal) { [Source] = "sync-ar-holidays" });
        var triggered = await dispatch.TakeAsync(CancellationToken.None);
        var nothingLeft = await dispatch.TakeAsync(CancellationToken.None);

        await Assert.That(requested.Status).IsEqualTo(HttpStatusCode.Accepted);
        await Assert.That(requested.Body.GetProperty("sourceId").GetString()).IsEqualTo(Source);
        await Assert.That(again.Status).IsEqualTo(HttpStatusCode.Accepted);
        await Assert.That(triggered).IsEquivalentTo(["sync-ar-holidays"]);
        await Assert.That(nothingLeft).IsEmpty();
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task A_requested_run_names_its_request_and_locates_the_source_runs_by_an_absolute_path(GeoDatabase database)
    {
        await SourceSync.HolidaysAsync(database, new SourceServer("holidays"));

        var requested = await SendAsync(database, valkey, HttpMethod.Post, $"/v1/geo/holiday-syncs/{Source}/runs");
        var located = await SendAsync(database, valkey, HttpMethod.Get, requested.Location!);
        var stored = await database.QueryAsync("SELECT id, requested_at FROM geo.sync_requests", reader => (reader.GetGuid(0), NodaTime.Instant.FromDateTimeUtc(DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc))));

        await Assert.That(requested.Location).IsEqualTo($"/v1/geo/holiday-syncs/{Source}/runs");
        await Assert.That(located.Status).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(requested.Body.TryGetProperty("id", out _)).IsFalse();
        await Assert.That(requested.Body.GetProperty("requestId").GetString()).IsEqualTo(stored[0].Item1.ToString());
        await Assert.That(requested.Body.GetProperty("requestedAt").GetString()).IsEqualTo(NodaTime.Text.InstantPattern.ExtendedIso.Format(stored[0].Item2));
        await Assert.That(requested.CacheControl).IsEqualTo("no-store");
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task The_holiday_sources_and_their_runs_are_never_stored_by_a_cache(GeoDatabase database)
    {
        await SourceSync.HolidaysAsync(database, new SourceServer("holidays"));

        var sources = await SendAsync(database, valkey, HttpMethod.Get, "/v1/geo/holiday-syncs");
        var runs = await SendAsync(database, valkey, HttpMethod.Get, $"/v1/geo/holiday-syncs/{Source}/runs");

        await Assert.That((sources.Status, sources.CacheControl, sources.ETag)).IsEqualTo((HttpStatusCode.OK, (string?)"no-store", (string?)null));
        await Assert.That((runs.Status, runs.CacheControl, runs.ETag)).IsEqualTo((HttpStatusCode.OK, (string?)"no-store", (string?)null));
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task The_Worker_composition_maps_a_requested_source_to_its_sync_job(GeoDatabase database)
    {
        await SourceSync.HolidaysAsync(database, new SourceServer("holidays"));
        await SendAsync(database, valkey, HttpMethod.Post, $"/v1/geo/holiday-syncs/{Source}/runs");
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Geo"] = database.ConnectionString }).Build());
        services.AddLogging().AddGeoCore().AddGeoSyncs(geo => geo.AddArgentina()).AddQuartz(quartz => quartz.AddGeoSyncJobs());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var triggered = await scope.ServiceProvider.GetRequiredService<SyncRequestDispatch>().TakeAsync(CancellationToken.None);

        await Assert.That(triggered).IsEquivalentTo(["sync-ar-holidays"]);
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task An_unknown_or_non_holiday_source_is_not_found(GeoDatabase database)
    {
        await SourceSync.TerritoryAsync(database, new SourceServer());

        var unknown = await SendAsync(database, valkey, HttpMethod.Post, "/v1/geo/holiday-syncs/nope/runs");
        var territory = await SendAsync(database, valkey, HttpMethod.Get, "/v1/geo/holiday-syncs/ar-georef-territory/runs");

        await Assert.That(Outcome(unknown)).IsEqualTo((HttpStatusCode.NotFound, (string?)"geo.holiday_sync.not_found"));
        await Assert.That(Outcome(territory)).IsEqualTo((HttpStatusCode.NotFound, (string?)"geo.holiday_sync.not_found"));
    }
}
