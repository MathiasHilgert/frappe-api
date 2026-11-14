using Frappe.Geo.Application;
using Frappe.Geo.IntegrationTests.Persistence;
using NodaTime;

namespace Frappe.Geo.IntegrationTests.Sync;

/// <summary>What the syncs report (S15): runs by source and outcome, and freshness and coverage read from the database.</summary>
public sealed class SyncMetricsTests
{
    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task Runs_are_counted_by_source_and_outcome_and_the_gauges_read_freshness_and_covered_years(GeoDatabase database)
    {
        var clock = new SettableClock(SystemClock.Instance.GetCurrentInstant());
        var (metrics, meter, provider) = SourceSync.Metrics(database, clock);
        await using var _ = provider;
        using var capture = new MetricCapture(meter);
        var government = new SourceServer("holidays");

        await SourceSync.HolidaysAsync(database, government, metrics);
        await SourceSync.HolidaysAsync(database, government, metrics);
        await SourceSync.BankingHolidaysAsync(database, SourceSync.Bcra(), metrics);
        clock.Now += Duration.FromHours(2);
        capture.Observe();

        const string Official = "ar-official-holidays";
        await Assert.That(capture.Sum(GeoMetrics.SyncRunsMetric, (GeoMetrics.SourceTag, Official), (GeoMetrics.OutcomeTag, "applied"))).IsEqualTo(1);
        await Assert.That(capture.Sum(GeoMetrics.SyncRunsMetric, (GeoMetrics.SourceTag, Official), (GeoMetrics.OutcomeTag, "unchanged"))).IsEqualTo(1);
        await Assert.That(capture.Sum(GeoMetrics.SyncRunsMetric, (GeoMetrics.SourceTag, "ar-banking-holidays"), (GeoMetrics.OutcomeTag, "applied"))).IsEqualTo(1);
        await Assert.That(capture.Sum(GeoMetrics.CoveredThroughMetric, (GeoMetrics.CountryTag, "AR"), (GeoMetrics.ScopeTag, "general"))).IsEqualTo(2026);
        await Assert.That(capture.Sum(GeoMetrics.CoveredThroughMetric, (GeoMetrics.CountryTag, "AR"), (GeoMetrics.ScopeTag, "banking"))).IsEqualTo(2026);
        await Assert.That(capture.Sum(GeoMetrics.LastSuccessAgeMetric, (GeoMetrics.SourceTag, Official))).IsBetween(7000, 7300);
        await Assert.That(capture.Measurements.Count(m => m.Name == GeoMetrics.LastSuccessAgeMetric)).IsEqualTo(2);
    }

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
