using Frappe.Geo.Application.Holidays;
using Frappe.Geo.Application.Sync;
using Frappe.Geo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Text;

namespace Frappe.Geo.Infrastructure.Sync;

/// <summary>The holiday sources as their sync state and audit tell them (<c>sync_resources</c>, <c>sync_runs</c>), and the run requests.</summary>
internal sealed class EfHolidaySyncs(GeoDbContext db, IClock clock) : IHolidaySyncs
{
    private const long TicksPerMicrosecond = NodaConstants.TicksPerMillisecond / 1000;

    public async Task<IReadOnlyList<HolidaySyncView>> ListAsync(CancellationToken cancellationToken)
    {
        var resources = await db.Set<SyncResourceRow>().AsNoTracking().Where(r => r.Kind == SyncKinds.Holidays).ToListAsync(cancellationToken);
        return [.. resources.GroupBy(r => r.SourceId, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal).Select(ToView)];
    }

    public Task<bool> ExistsAsync(string sourceId, CancellationToken cancellationToken) =>
        db.Set<SyncResourceRow>().AnyAsync(r => r.SourceId == sourceId && r.Kind == SyncKinds.Holidays, cancellationToken);

    public async Task<IReadOnlyList<SyncRunView>> RunsAsync(string sourceId, int limit, CancellationToken cancellationToken)
    {
        var runs = await db.Set<SyncRunRow>().AsNoTracking()
            .Where(r => r.SourceId == sourceId)
            .OrderByDescending(r => r.StartedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return [.. runs.Select(ToView)];
    }

    /// <summary>
    /// The request time is cut to what Postgres keeps (microseconds), so the answer shows the instant stored, with the
    /// precision every other stored instant (a run's start and end) is served with.
    /// </summary>
    public SyncRequestView RequestRun(string sourceId)
    {
        var now = clock.GetCurrentInstant();
        var stored = Instant.FromUnixTimeTicks(now.ToUnixTimeTicks() / TicksPerMicrosecond * TicksPerMicrosecond);
        var row = new SyncRequestRow { Id = Guid.CreateVersion7(), SourceId = sourceId, RequestedAt = stored };
        db.Add(row);
        return new SyncRequestView(row.Id, row.SourceId, row.RequestedAt);
    }

    /// <summary>A source's files are attempted together, so the latest attempt and success of any file are the source's.</summary>
    private static HolidaySyncView ToView(IGrouping<string, SyncResourceRow> files)
    {
        var latest = files.OrderByDescending(f => f.LastAttemptAt).First();
        return new HolidaySyncView(files.Key, latest.LastAttemptAt, latest.LastOutcome, files.Max(f => f.LastSuccessAt));
    }

    private static SyncRunView ToView(SyncRunRow run) => new(
        run.Id,
        run.StartedAt,
        run.FinishedAt,
        run.Outcome.ToString(),
        run.Inserted,
        run.Updated,
        run.Error,
        [.. run.Conflicts.Select(c => new OverrideConflict(c.Jurisdiction, c.Year, LocalDatePattern.Iso.Parse(c.Date).Value, c.Name))]);
}
