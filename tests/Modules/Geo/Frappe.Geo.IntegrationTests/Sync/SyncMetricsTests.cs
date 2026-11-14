using Frappe.Geo.Application;
using Frappe.Geo.IntegrationTests.Persistence;
using NodaTime;

namespace Frappe.Geo.IntegrationTests.Sync;

/// <summary>What the syncs report (S15): runs by source and outcome, and freshness and coverage read from the database.</summary>
public sealed class SyncMetricsTests
{
    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task A_failed_run_is_counted_and_reports_no_freshness_for_a_source_that_never_succeeded(GeoDatabase database)
    {
        var (metrics, meter, provider) = SourceSync.Metrics(database, SystemClock.Instance);
        await using var _ = provider;
        using var capture = new MetricCapture(meter);
        var government = new SourceServer("holidays");
        government.Unpublish("holidays-2026-es.json");

        await SourceSync.HolidaysAsync(database, government, metrics);
        capture.Observe();

        await Assert.That(capture.Sum(GeoMetrics.SyncRunsMetric, (GeoMetrics.SourceTag, "ar-official-holidays"), (GeoMetrics.OutcomeTag, "not_published"))).IsEqualTo(1);
        await Assert.That(capture.Measurements.Any(m => m.Name is GeoMetrics.LastSuccessAgeMetric or GeoMetrics.CoveredThroughMetric)).IsFalse();
    }
}
