using System.Net;
using Frappe.Geo.Infrastructure.Sync;
using Frappe.Geo.Infrastructure.Territory;
using Frappe.Geo.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Geo.IntegrationTests.Sync;

/// <summary>The one sync pipeline, driven by the Georef adapter: every outcome, its audit and what it leaves in the data.</summary>
public sealed class SnapshotSyncTests
{
    private static async Task<List<SyncRunRow>> RunsAsync(GeoDatabase database)
    {
        await using var db = database.NewContext();
        return await db.Set<SyncRunRow>().AsNoTracking().OrderBy(r => r.StartedAt).ToListAsync();
    }

    private static async Task<int> ActiveLocalitiesAsync(GeoDatabase database)
    {
        await using var db = database.NewContext();
        return await db.Set<LocalityRow>().CountAsync(l => l.RetiredAt == null);
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task A_first_run_applies_the_territory_and_audits_what_it_inserted(GeoDatabase database)
    {
        var outcome = await SourceSync.TerritoryAsync(database, new SourceServer());

        var run = (await RunsAsync(database)).Single();
        await Assert.That(outcome).IsEqualTo(SyncOutcome.Applied);
        await Assert.That(run.Inserted).IsEqualTo(3 + 7 + 4);
        await Assert.That(run.SourceId).IsEqualTo("ar-georef-territory");
        await Assert.That(await ActiveLocalitiesAsync(database)).IsEqualTo(4);
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task An_unchanged_source_costs_one_304_per_file_and_reads_nothing(GeoDatabase database)
    {
        var georef = new SourceServer();
        await SourceSync.TerritoryAsync(database, georef);
        georef.Requests.Clear();

        var outcome = await SourceSync.TerritoryAsync(database, georef);

        await Assert.That(outcome).IsEqualTo(SyncOutcome.Unchanged);
        await Assert.That(georef.Requests.Select(r => r.Status)).IsEquivalentTo(Enumerable.Repeat(HttpStatusCode.NotModified, 4));
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task When_one_file_changes_the_others_are_fetched_again_and_the_change_is_applied(GeoDatabase database)
    {
        var georef = new SourceServer();
        await SourceSync.TerritoryAsync(database, georef);
        georef.Replace("localidades.json", json => json.Replace("\"Villa Carlos Paz\"", "\"Villa Carlos Paz (renamed)\"", StringComparison.Ordinal));

        var outcome = await SourceSync.TerritoryAsync(database, georef);

        var run = (await RunsAsync(database))[^1];
        await Assert.That(outcome).IsEqualTo(SyncOutcome.Applied);
        await Assert.That(run.Updated).IsEqualTo(1);
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task A_download_that_would_retire_too_much_is_refused_and_changes_nothing(GeoDatabase database)
    {
        var georef = new SourceServer();
        await SourceSync.TerritoryAsync(database, georef);
        georef.Replace("localidades.json", json => json.Replace("\"Villa Carlos Paz\"", "\"Villa Carlos Paz \"", StringComparison.Ordinal).Replace("14091250", "14091251", StringComparison.Ordinal));

        var outcome = await SourceSync.TerritoryAsync(database, georef);
        var again = await SourceSync.TerritoryAsync(database, georef);

        await Assert.That(outcome).IsEqualTo(SyncOutcome.Refused);
        await Assert.That(again).IsEqualTo(SyncOutcome.Refused).Because("a refused run keeps the last success's validators, so it is read again");
        await Assert.That((await RunsAsync(database))[^1].Error).Contains("refused as truncated");
        await Assert.That(await ActiveLocalitiesAsync(database)).IsEqualTo(4);
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task A_source_slower_than_the_resilience_timeout_is_a_failed_run_that_is_audited_and_keeps_the_last_success(GeoDatabase database)
    {
        var georef = new SourceServer();
        await SourceSync.TerritoryAsync(database, georef);
        var validators = await ValidatorsAsync(database);
        georef.Replace("localidades.json", json => json.Replace("\"Villa Carlos Paz\"", "\"Villa Carlos Paz (renamed)\"", StringComparison.Ordinal));
        georef.Delay = TimeSpan.FromSeconds(2);

        var outcome = await SourceSync.TerritoryAsync(database, georef);

        var runs = await RunsAsync(database);
        await Assert.That(outcome).IsEqualTo(SyncOutcome.Failed);
        await Assert.That(runs.Count).IsEqualTo(2);
        await Assert.That(runs[^1].Outcome).IsEqualTo(SyncOutcome.Failed);
        await Assert.That(await ValidatorsAsync(database)).IsEquivalentTo(validators);
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task A_body_cut_mid_read_is_a_failed_run_that_is_audited(GeoDatabase database)
    {
        var georef = new SourceServer { CutBodyOf = "localidades.json" };

        var outcome = await SourceSync.TerritoryAsync(database, georef);

        await Assert.That(outcome).IsEqualTo(SyncOutcome.Failed);
        await Assert.That((await RunsAsync(database)).Single().Outcome).IsEqualTo(SyncOutcome.Failed);
        await Assert.That(await ActiveLocalitiesAsync(database)).IsEqualTo(0);
    }

    private static async Task<List<string>> ValidatorsAsync(GeoDatabase database)
    {
        await using var db = database.NewContext();
        return await db.Set<SyncResourceRow>().AsNoTracking().OrderBy(r => r.Resource).Select(r => r.Resource + "=" + r.ETag + "/" + r.Sha256).ToListAsync();
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task An_unpublished_file_changes_nothing_and_says_which(GeoDatabase database)
    {
        var georef = new SourceServer();
        georef.Unpublish("municipios.json");

        var outcome = await SourceSync.TerritoryAsync(database, georef);

        await Assert.That(outcome).IsEqualTo(SyncOutcome.NotPublished);
        await Assert.That((await RunsAsync(database)).Single().Error).Contains("municipios");
        await Assert.That(await ActiveLocalitiesAsync(database)).IsEqualTo(0);
    }
}
