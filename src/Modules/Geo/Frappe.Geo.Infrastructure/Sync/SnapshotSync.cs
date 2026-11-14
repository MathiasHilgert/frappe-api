using Frappe.Geo.Application.Sync;
using Frappe.Geo.Domain.ReferenceData;
using Frappe.Geo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;
using NodaTime.Text;
using Polly;

namespace Frappe.Geo.Infrastructure.Sync;

/// <summary>
/// The one sync pipeline every source of reference data runs through. A run downloads the source's files
/// conditionally, stops when nothing changed or a required file is not published (an optional one is left out), reads and validates the whole download, applies
/// it and records the new state in one transaction, and audits the run in <c>geo.sync_runs</c> whatever its outcome.
/// A refused or failed run changes no data and keeps the last success's validators, so the next run tries again.
/// </summary>
internal sealed partial class SnapshotSync<TSnapshot, TSource>(
    TSource source,
    ISnapshotTarget<TSnapshot> target,
    ConditionalFetcher fetcher,
    GeoDbContext db,
    IClock clock,
    SyncMetrics metrics,
    ILogger<SnapshotSync<TSnapshot, TSource>> logger)
    where TSource : ISnapshotSource<TSnapshot>
{
    public async Task<SyncOutcome> RunAsync(CancellationToken cancellationToken)
    {
        var run = new SyncRunRow { Id = Guid.CreateVersion7(), SourceId = source.Id, StartedAt = clock.GetCurrentInstant() };
        var states = await LoadStatesAsync(cancellationToken);
        try
        {
            var fetched = await FetchAsync(states, cancellationToken);
            await ConcludeAsync(run, states, fetched, cancellationToken);
        }
        catch (Exception exception) when (IsSourceFailure(exception) && !cancellationToken.IsCancellationRequested)
        {
            await DiscardAsync(states, cancellationToken);
            run.Outcome = SyncOutcome.Failed;
            run.Error = Truncate(exception.Message);
        }

        await RecordAsync(run, states, cancellationToken);
        metrics.Ran(source.Id, run.Outcome, run.Conflicts.Count);
        if (run.Error is null)
        {
            LogRun(logger, source.Id, run.Outcome, run.Inserted, run.Updated, run.Retired, run.Revived);
        }
        else
        {
            // A required file not published yet is expected (next year's calendar); a refused or failed run needs a look.
            LogRunError(logger, run.Outcome == SyncOutcome.NotPublished ? LogLevel.Information : LogLevel.Warning, source.Id, run.Outcome, run.Error);
        }

        return run.Outcome;
    }

    /// <summary>
    /// Drops everything the run changed (an applied snapshot included) and reloads the state rows as stored, so only
    /// the attempt is recorded and the last success's validators survive.
    /// </summary>
    private async Task DiscardAsync(Dictionary<string, SyncResourceRow> states, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var reloaded = await LoadStatesAsync(cancellationToken);
        states.Clear();
        foreach (var (name, row) in reloaded)
        {
            states[name] = row;
        }
    }

    private async Task<Dictionary<string, SyncResourceRow>> LoadStatesAsync(CancellationToken cancellationToken)
    {
        var stored = await db.Set<SyncResourceRow>().Where(r => r.SourceId == source.Id).ToDictionaryAsync(r => r.Resource, StringComparer.Ordinal, cancellationToken);

        foreach (var name in source.Resources.Select(r => r.Name).Where(name => !stored.ContainsKey(name)))
        {
            var row = new SyncResourceRow { SourceId = source.Id, Resource = name };
            db.Add(row);
            stored[name] = row;
        }

        foreach (var row in stored.Values.Where(r => r.Kind != target.Kind))
        {
            row.Kind = target.Kind;
        }

        return stored;
    }

    /// <summary>Every file, conditionally; when one changed, the unchanged ones again in full, since a snapshot is read whole.</summary>
    private async Task<Dictionary<string, FetchResult>> FetchAsync(Dictionary<string, SyncResourceRow> states, CancellationToken cancellationToken)
    {
        var fetched = new Dictionary<string, FetchResult>(StringComparer.Ordinal);
        foreach (var resource in source.Resources)
        {
            var state = states[resource.Name];
            fetched[resource.Name] = await fetcher.FetchAsync(resource.Location, state.ETag, state.LastModified, cancellationToken);
        }

        // A file with new content, or an optional one withdrawn since the last success, changes what the source reads.
        if (fetched.Any(f => f.Value.Status == FetchStatus.Content || (f.Value.Status == FetchStatus.NotPublished && states[f.Key].Sha256 is not null)))
        {
            foreach (var resource in source.Resources.Where(r => fetched[r.Name].Status == FetchStatus.NotModified))
            {
                fetched[resource.Name] = await fetcher.FetchAsync(resource.Location, null, null, cancellationToken);
            }
        }

        return fetched;
    }

    private async Task ConcludeAsync(SyncRunRow run, Dictionary<string, SyncResourceRow> states, Dictionary<string, FetchResult> fetched, CancellationToken cancellationToken)
    {
        var missing = source.Resources.Where(r => !r.Optional && fetched[r.Name].Status == FetchStatus.NotPublished).Select(r => r.Name).ToArray();
        if (missing.Length > 0)
        {
            run.Outcome = SyncOutcome.NotPublished;
            run.Error = "Not published: " + string.Join(", ", missing);
            return;
        }

        if (fetched.All(f => f.Value.Status == FetchStatus.NotModified || f.Value.Sha256 == states[f.Key].Sha256))
        {
            run.Outcome = SyncOutcome.Unchanged;
            MarkSuccess(states, fetched);
            return;
        }

        var snapshot = source.Read(fetched.Where(f => f.Value.Status != FetchStatus.NotPublished).ToDictionary(f => f.Key, f => f.Value.Body, StringComparer.Ordinal));
        if (!snapshot.IsSuccess)
        {
            run.Outcome = SyncOutcome.Refused;
            run.Error = Truncate(SnapshotRules.Describe(snapshot.Errors));
            return;
        }

        var changes = await target.ApplyAsync(snapshot.Value, cancellationToken);
        if (changes.RetiresTooMuch)
        {
            await DiscardAsync(states, cancellationToken);
            run.Outcome = SyncOutcome.Refused;
            run.Error = $"The download would retire {changes.Retired} records, more than {ReferenceDataPlan.MaxRetiredShare:P0} of a table: refused as truncated.";
            return;
        }

        (run.Inserted, run.Updated, run.Retired, run.Revived) = (changes.Inserted, changes.Updated, changes.Retired, changes.Revived);
        foreach (var conflict in changes.Conflicts)
        {
            var date = LocalDatePattern.Iso.Format(conflict.Date);
            run.Conflicts.Add(new SyncConflictRow { Jurisdiction = conflict.Jurisdiction, Year = conflict.Year, Date = date, Name = conflict.Name });
            LogConflict(logger, source.Id, conflict.Jurisdiction, date, conflict.Name);
        }

        run.Outcome = SyncOutcome.Applied;
        MarkSuccess(states, fetched);
    }

    private void MarkSuccess(Dictionary<string, SyncResourceRow> states, Dictionary<string, FetchResult> fetched)
    {
        var now = clock.GetCurrentInstant();
        foreach (var (name, result) in fetched)
        {
            var state = states[name];
            state.ETag = result.ETag;
            state.LastModified = result.LastModified;
            state.Sha256 = result.Status == FetchStatus.NotPublished ? null : result.Sha256 ?? state.Sha256;
            state.LastSuccessAt = now;
        }
    }

    /// <summary>The run's audit row and every file's attempt, saved with the applied snapshot (if any) in one transaction.</summary>
    private async Task RecordAsync(SyncRunRow run, Dictionary<string, SyncResourceRow> states, CancellationToken cancellationToken)
    {
        run.FinishedAt = clock.GetCurrentInstant();
        foreach (var state in states.Values)
        {
            state.LastAttemptAt = run.FinishedAt;
            state.LastOutcome = run.Outcome.ToString();
        }

        db.Add(run);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Whatever a source's failure looks like through the resilience pipeline: an error status or a refused connection
    /// (<see cref="HttpRequestException"/>), a body cut mid-read (<see cref="IOException"/>, such as
    /// <see cref="HttpIOException"/>), a timeout or an open circuit (<see cref="ExecutionRejectedException"/>), or the
    /// body's own time bound (<see cref="OperationCanceledException"/> while the run itself was not cancelled).
    /// </summary>
    private static bool IsSourceFailure(Exception exception) =>
        exception is HttpRequestException or IOException or ExecutionRejectedException or OperationCanceledException;

    private static string Truncate(string text) => text.Length <= SyncRunRow.MaxErrorLength ? text : text[..SyncRunRow.MaxErrorLength];

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sync of {Source} changed {Jurisdiction} {Date} \"{Name}\" under a manual override: the override still wins")]
    private static partial void LogConflict(ILogger logger, string source, string jurisdiction, string date, string name);

    /// <summary>
    /// A run that ended without an error. The counts are the target's records (<see cref="SnapshotChanges"/>): territory
    /// rows for a territory source, whole calendars (a jurisdiction's year and scope) for a holiday source, never single
    /// observances.
    /// </summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "Sync of {Source}: {Outcome} (records inserted {Inserted}, updated {Updated}, retired {Retired}, revived {Revived})")]
    private static partial void LogRun(ILogger logger, string source, SyncOutcome outcome, int inserted, int updated, int retired, int revived);

    /// <summary>A run that applied nothing for a reason (not published, refused or failed), and the reason.</summary>
    [LoggerMessage(Message = "Sync of {Source}: {Outcome}: {Error}")]
    private static partial void LogRunError(ILogger logger, LogLevel level, string source, SyncOutcome outcome, string error);
}
