using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NodaTime;

namespace Frappe.Messaging.Relay;

/// <summary>An integration event leaving the module, with its CloudEvents attributes.</summary>
public sealed record OutgoingMessage(
    Guid Id,
    string Type,
    string Source,
    string DataContentType,
    ReadOnlyMemory<byte> Data,
    Instant Time,
    string? TraceParent,
    string? TraceState);

/// <summary>
/// Outbound port of the relay: hands one message to the broker and returns only once it is stored there.
/// It throws <see cref="MessageRejectedException"/> when the broker refuses this particular message; any other
/// exception means the broker is unavailable and says nothing about the message.
/// </summary>
public interface IMessagePublisher
{
    Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken);
}

/// <summary>The broker refused this message (too large, invalid subject, rejected by the stream). Retrying the same bytes may never work.</summary>
public sealed class MessageRejectedException : Exception
{
    public MessageRejectedException()
    {
    }

    public MessageRejectedException(string message)
        : base(message)
    {
    }

    public MessageRejectedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Polling publisher with leases: a short transaction claims a batch of due rows (<c>FOR UPDATE SKIP LOCKED</c>, so
/// several relays never take the same row) and commits; the rows are then published in sequence order with no
/// database transaction or lock held during broker I/O; a second short save records what happened.
/// Only a <see cref="MessageRejectedException"/> counts against a row: it gets its next attempt time (backoff) and
/// the pass ends; from the next pass on the row is not due yet, so the rows behind it go out first. After
/// <see cref="OutboxRelayOptions.MaxAttempts"/> it is parked. Any other
/// failure means the broker is unavailable: the row is left untouched (an outage must never park healthy messages)
/// and the pass ends. Running out of the pass budget is not a failure either.
/// </summary>
public sealed partial class OutboxRelay<TContext>(
    IServiceScopeFactory scopes,
    IMessagePublisher publisher,
    TimeProvider time,
    OutboxRelayOptions options,
    ILogger<OutboxRelay<TContext>> logger)
    where TContext : DbContext
{
    /// <summary>Publishes one batch and returns how many messages were published.</summary>
    public async Task<int> PublishPendingAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();

        var now = Now();
        var batch = await db.Set<OutboxMessage>()
            .FromSqlRaw(ClaimSql(db), options.BatchSize, options.MaxAttempts, now, now + Duration.FromTimeSpan(options.LeaseDuration))
            .ToListAsync(cancellationToken);
        if (batch.Count == 0)
        {
            return 0;
        }

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

    /// <summary>Claims due, unleased rows in one statement and returns them; committed on its own.</summary>
    private static string ClaimSql(DbContext db)
    {
        var table = db.Model.FindEntityType(typeof(OutboxMessage))
            ?? throw new InvalidOperationException($"{typeof(TContext).Name} has no outbox; call modelBuilder.AddOutbox().");
        var name = $"\"{table.GetSchema()}\".\"{table.GetTableName()}\"";
        return "UPDATE " + name + " SET leased_until = {3} WHERE id IN ("
            + " SELECT id FROM " + name
            + " WHERE published_at IS NULL AND attempts < {1}"
            + " AND (next_attempt_at IS NULL OR next_attempt_at <= {2})"
            + " AND (leased_until IS NULL OR leased_until <= {2})"
            + " ORDER BY sequence LIMIT {0} FOR UPDATE SKIP LOCKED)"
            + " RETURNING *";
    }

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

    [LoggerMessage(Level = LogLevel.Warning, Message = "The outbox lease of {Rows} rows expired before they were settled; another relay owns them now")]
    private static partial void LogLeaseLost(ILogger logger, Exception exception, int rows);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox message {MessageId} ({Type}) failed {Attempts} times and is parked; it will not be retried automatically")]
    private static partial void LogParked(ILogger logger, Exception exception, Guid messageId, string type, int attempts);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The broker rejected outbox message {MessageId} ({Type}), attempt {Attempts}; it will be retried later")]
    private static partial void LogRejected(ILogger logger, Exception exception, Guid messageId, string type, int attempts);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The broker is unavailable while publishing {MessageId} ({Type}); the outbox is untouched and the next pass retries")]
    private static partial void LogBrokerUnavailable(ILogger logger, Exception exception, Guid messageId, string type);
}
