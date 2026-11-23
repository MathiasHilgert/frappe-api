using System.Diagnostics;
using Frappe.Messaging.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NodaTime;

namespace Frappe.Messaging.Relay;

/// <summary>
/// Polling publisher with leases: a short transaction claims a batch of due rows (<c>FOR UPDATE SKIP LOCKED</c>, so
/// several relays never take the same row) and commits; the rows are then published in sequence order with no
/// database transaction or lock held during broker I/O; a second short save records what happened.
/// Only a <see cref="MessageRejectedException"/> counts against a row: it gets its next attempt time (backoff) and
/// the pass ends; from the next pass on the row is not due yet, so the rows behind it go out first. After
/// <see cref="OutboxRelayOptions.MaxAttempts"/> it is parked: it leaves the pending rows (<see cref="OutboxModel.PendingFilter"/>)
/// for good, so no claim reads it again, and stays in the outbox until an operator requeues or deletes it. Any other
/// failure means the broker is unavailable: the row is left untouched (an outage must never park healthy messages)
/// and the pass ends. Running out of the pass budget is not a failure either.
/// Every pass counts what it published in <see cref="MessagingMetrics.RelayedMetric"/>. The oldest unpublished message
/// (<see cref="MessagingMetrics.OutboxOldestAgeMetric"/>) and the parked count (<see cref="MessagingMetrics.OutboxParkedMetric"/>)
/// are looked up after a pass that claimed rows, and otherwise once per <see cref="OutboxRelayOptions.SampleInterval"/>, so an
/// idle module costs one query per poll instead of two. Both are labelled with <paramref name="module"/>.
/// </summary>
/// <param name="scopes">Creates the scope each pass reads the module's context from.</param>
/// <param name="publisher">Hands messages to the broker.</param>
/// <param name="time">The clock for leases, backoff and the pass budget.</param>
/// <param name="options">Batch, timeouts and backoff.</param>
/// <param name="module">The module whose outbox this is, for example <c>orders</c>; the metrics' label.</param>
/// <param name="metrics">The messaging instruments.</param>
/// <param name="logger">The logger.</param>
public sealed partial class OutboxRelay<TContext>(
    IServiceScopeFactory scopes,
    IMessagePublisher publisher,
    TimeProvider time,
    OutboxRelayOptions options,
    string module,
    MessagingMetrics metrics,
    ILogger<OutboxRelay<TContext>> logger)
    where TContext : DbContext
{
    /// <summary>
    /// When the outbox was last looked up for the metrics; null until the first pass. One relay per module runs one pass at a time.
    /// </summary>
    private Instant? _sampledAt;

    /// <summary>Publishes one batch and returns how many messages were published.</summary>
    public async Task<int> PublishPendingAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();

        var now = Now();
        var batch = await db.Set<OutboxMessage>()
            .FromSqlRaw(ClaimSql(db), options.BatchSize, now, now + Duration.FromTimeSpan(options.LeaseDuration))
            .ToListAsync(cancellationToken);
        var published = batch.Count == 0 ? 0 : await PublishBatchAsync(db, batch, cancellationToken);
        metrics.Relayed(module, published);
        if (!cancellationToken.IsCancellationRequested && SampleDue(claimed: batch.Count > 0))
        {
            await SampleAsync(db, cancellationToken);
        }

        return published;
    }

    /// <summary>
    /// Whether this pass looks the outbox up for the gauges: always after a pass that claimed rows (what it published or
    /// parked changes both at once), otherwise on the first pass and then once per <see cref="OutboxRelayOptions.SampleInterval"/>.
    /// A clock set back counts as due, so a lookup is never postponed by more than one interval.
    /// </summary>
    private bool SampleDue(bool claimed)
    {
        var now = Now();
        if (!claimed && _sampledAt is { } last && now >= last && now - last < Duration.FromTimeSpan(options.SampleInterval))
        {
            return false;
        }

        _sampledAt = now;
        return true;
    }

    /// <summary>
    /// Feeds the outbox age and parked gauges. It is only a metric: a failed lookup is logged and the gauges keep their
    /// last values (the age keeps aging), and the pass, which may already have published, still succeeds.
    /// </summary>
    private async Task SampleAsync(DbContext db, CancellationToken cancellationToken)
    {
        try
        {
            var sample = await db.Database.SqlQueryRaw<OutboxSample>(SampleSql(db)).SingleAsync(cancellationToken);
            metrics.OldestUnpublished(module, sample.Oldest);
            metrics.Parked(module, sample.Parked);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogSampleFailed(logger, exception, module);
        }
    }

    /// <summary>Publishes a claimed batch in sequence order and settles it; returns how many went out.</summary>
    private async Task<int> PublishBatchAsync(DbContext db, List<OutboxMessage> batch, CancellationToken cancellationToken)
    {
        var published = 0;
        var passStarted = time.GetTimestamp();
        foreach (var message in batch.OrderBy(m => m.Sequence))
        {
            var remaining = options.MaxPassDuration - time.GetElapsedTime(passStarted);
            if (remaining <= TimeSpan.Zero)
            {
                message.ReleaseLease();
                continue;
            }

            if (cancellationToken.IsCancellationRequested || !await PublishOneAsync(message, remaining, cancellationToken))
            {
                foreach (var untouched in batch.Where(m => m.PublishedAt is null && m.LeasedUntil is not null))
                {
                    untouched.ReleaseLease();
                }

                break;
            }

            published++;
        }

        await SettleAsync(db);
        return published;
    }

    /// <summary>
    /// Records what happened, even if the caller is stopping. <c>leased_until</c> is a concurrency token: a row whose
    /// lease expired and that another relay re-claimed is left to that relay, and only that row is dropped from the
    /// settle; every other row is still recorded (one conflict must not undo the whole batch).
    /// </summary>
    private async Task SettleAsync(DbContext db)
    {
        while (true)
        {
            try
            {
                await db.SaveChangesAsync(CancellationToken.None);
                return;
            }
            catch (DbUpdateConcurrencyException conflict)
            {
                LogLeaseLost(logger, conflict, conflict.Entries.Count);
                foreach (var entry in conflict.Entries)
                {
                    entry.State = EntityState.Detached;
                }
            }
        }
    }

    /// <summary>Publishes one message; false ends the pass (the broker refused it or is unavailable).</summary>
    private async Task<bool> PublishOneAsync(OutboxMessage message, TimeSpan remaining, CancellationToken cancellationToken)
    {
        using var activity = MessagingTelemetry.StartChildOf(
            $"publish {message.Type}", ActivityKind.Producer, message.TraceParent, message.TraceState);
        activity?.SetTag("messaging.system", "nats");
        activity?.SetTag("messaging.destination.name", message.Type);
        activity?.SetTag("messaging.message.id", message.Id.ToString());

        try
        {
            using var timeout = new CancellationTokenSource(remaining < options.PublishTimeout ? remaining : options.PublishTimeout, time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            await publisher.PublishAsync(ToOutgoing(message, activity), linked.Token);
            message.MarkPublished(Now());
            return true;
        }
        catch (MessageRejectedException rejected)
        {
            var parked = message.Attempts + 1 >= options.MaxAttempts;
            var retryAt = parked
                ? (Instant?)null
                : Now() + Duration.FromTimeSpan(options.RetryBackoff[Math.Min(message.Attempts, options.RetryBackoff.Count - 1)]);
            message.RecordFailure($"{rejected.GetType().Name}: {rejected.Message}", retryAt);
            activity?.SetStatus(ActivityStatusCode.Error, rejected.Message);
            if (parked)
            {
                LogParked(logger, rejected, message.Id, message.Type, message.Attempts);
            }
            else
            {
                LogRejected(logger, rejected, message.Id, message.Type, message.Attempts);
            }

            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The host is stopping: give the row back and let the settle record what already went out.
            message.ReleaseLease();
            return false;
        }
        catch (Exception unavailable)
        {
            message.ReleaseLease();
            activity?.SetStatus(ActivityStatusCode.Error, unavailable.Message);
            LogBrokerUnavailable(logger, unavailable, message.Id, message.Type);
            return false;
        }
    }

    private Instant Now() => Instant.FromDateTimeOffset(time.GetUtcNow());

    /// <summary>
    /// Claims due, unleased rows in one statement and returns them; committed on its own. Its first condition is
    /// <see cref="OutboxModel.PendingFilter"/> word for word, so <see cref="OutboxModel.PendingIndex"/> serves it and a
    /// parked row is never read. <see cref="OutboxRelayOptions.MaxAttempts"/> is not part of it: a parked row has no next
    /// attempt time, and a row that reached a lowered limit gets one more attempt and is parked if it fails again.
    /// </summary>
    private static string ClaimSql(DbContext db)
    {
        var name = TableName(db);
        return "UPDATE " + name + " SET leased_until = {2} WHERE id IN ("
            + " SELECT id FROM " + name
            + " WHERE " + OutboxModel.PendingFilter
            + " AND (next_attempt_at IS NULL OR next_attempt_at <= {1})"
            + " AND (leased_until IS NULL OR leased_until <= {1})"
            + " ORDER BY sequence LIMIT {0} FOR UPDATE SKIP LOCKED)"
            + " RETURNING *";
    }

    /// <summary>
    /// One round trip for both gauges, each part read from its own partial index: the first row by sequence of the
    /// pending rows and of the parked rows (the older of the two is the oldest unpublished message, whatever keeps it
    /// waiting, give or take concurrent commits), and how many rows are parked.
    /// </summary>
    private static string SampleSql(DbContext db)
    {
        var name = TableName(db);
        return "SELECT LEAST("
            + " (SELECT occurred_at FROM " + name + " WHERE " + OutboxModel.PendingFilter + " ORDER BY sequence LIMIT 1),"
            + " (SELECT occurred_at FROM " + name + " WHERE " + OutboxModel.ParkedFilter + " ORDER BY sequence LIMIT 1)) AS \"Oldest\","
            + " (SELECT count(*) FROM " + name + " WHERE " + OutboxModel.ParkedFilter + ") AS \"Parked\"";
    }

    private static string TableName(DbContext db)
    {
        var table = db.Model.FindEntityType(typeof(OutboxMessage))
            ?? throw new InvalidOperationException($"{typeof(TContext).Name} has no outbox; call modelBuilder.AddOutbox().");
        return $"\"{table.GetSchema()}\".\"{table.GetTableName()}\"";
    }

    /// <summary>What <see cref="SampleSql"/> returns.</summary>
    private sealed record OutboxSample(Instant? Oldest, long Parked);

    /// <summary>The publish span becomes the parent consumers see; without a listener, the stored context is passed on.</summary>
    private static OutgoingMessage ToOutgoing(OutboxMessage message, Activity? activity) => new(
        message.Id,
        message.Type,
        message.Source,
        message.DataContentType,
        System.Text.Encoding.UTF8.GetBytes(message.Data),
        message.OccurredAt,
        activity is { IdFormat: ActivityIdFormat.W3C } ? activity.Id : message.TraceParent,
        activity?.TraceStateString ?? message.TraceState);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The outbox lease of {Rows} rows expired before they were settled; "
        + "another relay owns them now")]
    private static partial void LogLeaseLost(ILogger logger, Exception exception, int rows);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Outbox message {MessageId} ({Type}) failed {Attempts} times and is "
        + "parked; it will not be retried automatically")]
    private static partial void LogParked(ILogger logger, Exception exception, Guid messageId, string type, int attempts);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The broker rejected outbox message {MessageId} ({Type}), attempt "
        + "{Attempts}; it will be retried later")]
    private static partial void LogRejected(ILogger logger, Exception exception, Guid messageId, string type, int attempts);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The oldest unpublished and the parked messages of {Module}'s outbox "
        + "could not be looked up; their gauges keep their last values")]
    private static partial void LogSampleFailed(ILogger logger, Exception exception, string module);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The broker is unavailable while publishing {MessageId} ({Type}); the "
        + "outbox is untouched and the next pass retries")]
    private static partial void LogBrokerUnavailable(ILogger logger, Exception exception, Guid messageId, string type);
}
