using System.Diagnostics.Metrics;
using Frappe.Geo.Application;
using Frappe.Geo.Application.Territory;
using Frappe.Geo.Countries.Argentina.Territory;
using Frappe.Geo.Infrastructure.Sync;
using Frappe.Geo.Infrastructure.Territory;
using Frappe.Geo.IntegrationTests.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NodaTime;

namespace Frappe.Geo.IntegrationTests.Sync;

/// <summary>What the territory sync reports (S15): runs by source and outcome, and the source's freshness read from the database.</summary>
public sealed class TerritorySyncMetricsTests
{
    private const string Territory = "ar-georef-territory";

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task Runs_are_counted_by_source_and_outcome_and_the_gauge_reads_the_last_success(GeoDatabase database)
    {
        var (metrics, meter, provider) = Metrics(database);
        await using var _ = provider;
        using var capture = new MetricCapture(meter);
        var georef = new SourceServer();

        await SyncAsync(database, georef, metrics);
        await SyncAsync(database, georef, metrics);
        capture.Observe();

        await Assert.That(capture.Sum(GeoMetrics.SyncRunsMetric, (GeoMetrics.SourceTag, Territory), (GeoMetrics.OutcomeTag, "applied"))).IsEqualTo(1);
        await Assert.That(capture.Sum(GeoMetrics.SyncRunsMetric, (GeoMetrics.SourceTag, Territory), (GeoMetrics.OutcomeTag, "unchanged"))).IsEqualTo(1);
        await Assert.That(capture.Sum(GeoMetrics.LastSuccessAgeMetric, (GeoMetrics.SourceTag, Territory))).IsBetween(0, 300);
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task A_run_that_finds_a_file_missing_is_counted_and_reports_no_freshness_for_a_source_that_never_succeeded(GeoDatabase database)
    {
        var (metrics, meter, provider) = Metrics(database);
        await using var _ = provider;
        using var capture = new MetricCapture(meter);
        var georef = new SourceServer();
        georef.Unpublish("municipios.json");

        await SyncAsync(database, georef, metrics);
        capture.Observe();

        await Assert.That(capture.Sum(GeoMetrics.SyncRunsMetric, (GeoMetrics.SourceTag, Territory), (GeoMetrics.OutcomeTag, "not_published"))).IsEqualTo(1);
        await Assert.That(capture.Measurements.Any(m => m.Name == GeoMetrics.LastSuccessAgeMetric)).IsFalse();
    }

    private static (SyncMetrics Metrics, Meter Meter, ServiceProvider Provider) Metrics(GeoDatabase database)
    {
        var provider = new ServiceCollection().AddMetrics().AddScoped(_ => database.NewContext()).BuildServiceProvider();
        var geo = new GeoMetrics(provider.GetRequiredService<IMeterFactory>());
        return (new SyncMetrics(geo, provider.GetRequiredService<IServiceScopeFactory>(), SystemClock.Instance), geo.Meter, provider);
    }

    private static async Task SyncAsync(GeoDatabase database, SourceServer georef, SyncMetrics metrics)
    {
        await using var db = database.NewContext();
        var sync = new SnapshotSync<TerritorySnapshot, GeorefTerritorySource>(
            new GeorefTerritorySource(Options.Create(new GeorefOptions())),
            new TerritoryTarget(db, SystemClock.Instance),
            new ConditionalFetcher(new HttpClient(georef, disposeHandler: false)),
            db,
            SystemClock.Instance,
            metrics,
            NullLogger<SnapshotSync<TerritorySnapshot, GeorefTerritorySource>>.Instance);
        await sync.RunAsync(CancellationToken.None);
    }
}
