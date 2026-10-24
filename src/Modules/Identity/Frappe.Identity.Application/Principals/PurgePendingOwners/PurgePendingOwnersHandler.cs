using Frappe.Identity.Application.Security;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Principals;
using Frappe.SharedKernel.Domain;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace Frappe.Identity.Application.Principals;

public static partial class PurgePendingOwnersHandler
{
    /// <summary>Bounds one run; whatever is left is picked up by the next run.</summary>
    public const int BatchSize = 500;

    /// <summary>Candidates read per query. Paging is keyset on (registered at, id), so it always moves forward.</summary>
    public const int PageSize = 100;

    // The query applies the purge rule (pending, registered before the cutoff); the aggregate still decides, so the rule
    // has one owner. Each principal is loaded and saved on its own: a conflict (someone verified or changed it
    // meanwhile) skips that principal only, and the keyset cursor moves past it, so it can never stall a run. Every
    // statement runs inside an isolated step (a savepoint): an unexpected failure loading or saving one principal
    // (a constraint, a trigger, a bug) rolls back only that principal; the run logs, counts and moves on, and the
    // next run retries it. A failed page query ends the run but keeps what it already purged. Only cancellation
    // fails the whole run.
    // A deleted aggregate leaves the change tracker before its events can be scraped, so they are returned as
    // cascading messages instead: Wolverine writes them to the outbox in this handler's transaction. Nothing waits
    // for an answer (a recurring job publishes this), so the messages are all it returns.
    public static async Task<OutgoingMessages> Handle(
        PurgePendingOwners command,
        IPrincipalRepository repository,
        IIdentityTelemetry telemetry,
        TimeProvider clock,
        ILogger<PurgePendingOwners> logger,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var cutoff = now - Principal.PendingGracePeriod;
        var events = new OutgoingMessages();
        var purged = 0;
        var attempted = 0;
        PurgeCandidate? after = null;

        while (attempted < BatchSize)
        {
            var page = await NextPageAsync(
                repository, telemetry, logger, cutoff, after, Math.Min(PageSize, BatchSize - attempted), cancellationToken);
            if (page is null)
                break;

            foreach (var id in page.Select(c => c.Id))
            {
                attempted++;
                Outcome outcome;
                try
                {
                    // The events join the outbox only once the isolated step committed to its savepoint.
                    (outcome, var raised) = await repository.IsolateAsync(
                        token => TryPurgeAsync(id, repository, now, token), cancellationToken);
                    events.AddRange(raised);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    telemetry.PurgeFailed();
                    LogFailed(logger, exception, id);
                    continue;
                }

                switch (outcome)
                {
                    case Outcome.Purged:
                        purged++;
                        break;
                    case Outcome.Conflict:
                        telemetry.PurgeSkippedConflict();
                        LogSkipped(logger, id, IdentityErrors.ConcurrentChange.Code);
                        break;
                    default:
                        LogSkipped(logger, id, "refused by the aggregate");
                        break;
                }
            }

            if (page.Count < PageSize)
                break;
            after = page[^1];
        }

        telemetry.PurgedOwners(purged);
        LogPurged(logger, purged, attempted);
        return events;
    }

    private enum Outcome { Purged, Refused, Conflict }

    /// <summary>The next page of candidates, or null when the query failed (logged and counted).</summary>
    private static async Task<IReadOnlyList<PurgeCandidate>?> NextPageAsync(
        IPrincipalRepository repository, IIdentityTelemetry telemetry, ILogger logger, DateTimeOffset cutoff,
        PurgeCandidate? after, int size, CancellationToken cancellationToken)
    {
        try
        {
            return await repository.IsolateAsync(
                token => repository.ListPurgeCandidatesAsync(cutoff, after, size, token), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            telemetry.PurgeFailed();
            LogPageFailed(logger, exception);
            return null;
        }
    }

    private static async Task<(Outcome, object[])> TryPurgeAsync(
        Guid id, IPrincipalRepository repository, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var principal = await repository.GetByIdAsync(id, cancellationToken);
        // Already gone (verified and deleted elsewhere is impossible, but a concurrent purge is not): nothing to do.
        if (principal is null || principal.PurgeAbandonedRegistration(now).IsFailure)
            return (Outcome.Refused, []);

        var raised = ((IHasDomainEvents)principal).DequeueDomainEvents();
        repository.Remove(principal);
        var saved = await repository.SaveAsync(cancellationToken);
        return saved.IsFailure ? (Outcome.Conflict, []) : (Outcome.Purged, [.. raised]);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Purge skipped pending owner {PrincipalId}: {Reason}")]
    private static partial void LogSkipped(ILogger logger, Guid principalId, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Purge failed for pending owner {PrincipalId}; the next run retries it")]
    private static partial void LogFailed(ILogger logger, Exception exception, Guid principalId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Purge stopped: the candidate query failed; the next run continues")]
    private static partial void LogPageFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Purged {Purged} abandoned registrations of {Attempted} candidates")]
    private static partial void LogPurged(ILogger logger, int purged, int attempted);
}
