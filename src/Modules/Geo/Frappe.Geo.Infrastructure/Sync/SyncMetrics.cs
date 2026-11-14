using System.Diagnostics.Metrics;
using Frappe.Geo.Application;
using Frappe.Geo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;

namespace Frappe.Geo.Infrastructure.Sync;

/// <summary>
/// What the syncs report (S15): every run by source and outcome with its override conflicts, and an observable gauge
/// read from the database when the exporter collects: each source's last success age. One reading serves the gauge for
/// <see cref="ReadingLifetime"/>, so a collection costs at most one small query a minute. A singleton of the host that syncs (the Worker), so only it reports them.
/// </summary>
internal sealed class SyncMetrics
{
    /// <summary>How long one reading of the database serves the gauges.</summary>
    public static readonly Duration ReadingLifetime = Duration.FromMinutes(1);

    private readonly GeoMetrics _metrics;
    private readonly IServiceScopeFactory _scopes;
    private readonly IClock _clock;
    private readonly Lock _gate = new();
    private Reading? _reading;

    public SyncMetrics(GeoMetrics metrics, IServiceScopeFactory scopes, IClock clock)
    {
        _metrics = metrics;
        _scopes = scopes;
        _clock = clock;
        metrics.Meter.CreateObservableGauge(
            GeoMetrics.LastSuccessAgeMetric,
            LastSuccessAges,
            unit: "s",
            description: "Seconds since each source's last successful sync run (applied or unchanged).");
    }

    /// <summary>A run of <paramref name="source"/> ended with <paramref name="outcome"/>.</summary>
    public void Ran(string source, SyncOutcome outcome, int conflicts) =>
        _metrics.SyncRan(source, OutcomeName(outcome), conflicts);

    public static string OutcomeName(SyncOutcome outcome) => outcome switch
    {
        SyncOutcome.Applied => "applied",
        SyncOutcome.Unchanged => "unchanged",
        SyncOutcome.NotPublished => "not_published",
        SyncOutcome.Refused => "refused",
        _ => "failed",
    };

    private IEnumerable<Measurement<double>> LastSuccessAges()
    {
        var reading = Read();
        var now = _clock.GetCurrentInstant();
        return reading?.LastSuccess.Select(s => new Measurement<double>(
            Math.Max(0, (now - s.Value).TotalSeconds),
            new KeyValuePair<string, object?>(GeoMetrics.SourceTag, s.Key))) ?? [];
    }

    /// <summary>The current reading, read again once it is older than <see cref="ReadingLifetime"/>; none when the database cannot be read.</summary>
    private Reading? Read()
    {
        lock (_gate)
        {
            var now = _clock.GetCurrentInstant();
            if (_reading is not null && now - _reading.At < ReadingLifetime)
            {
                return _reading;
            }

            try
            {
                using var scope = _scopes.CreateScope();
                _reading = Load(scope.ServiceProvider.GetRequiredService<GeoDbContext>(), now);
            }
#pragma warning disable CA1031 // A gauge must never fail the exporter: an unreadable database reports nothing this time.
            catch (Exception)
#pragma warning restore CA1031
            {
                _reading = null;
            }

            return _reading;
        }
    }

    private static Reading Load(GeoDbContext db, Instant now)
    {
        // The table is small (one row per file): the keys are read and aggregated in memory, since
        // the value-converted columns do not translate to SQL aggregates.
        var lastSuccess = db.Set<SyncResourceRow>().AsNoTracking()
            .Where(r => r.LastSuccessAt != null)
            .Select(r => new { r.SourceId, r.LastSuccessAt })
            .ToList()
            .GroupBy(r => r.SourceId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Max(r => r.LastSuccessAt!.Value), StringComparer.Ordinal);
        return new Reading(now, lastSuccess);
    }

    private sealed record Reading(Instant At, Dictionary<string, Instant> LastSuccess);
}
