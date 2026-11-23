using System.Diagnostics;
using System.Text.Json;
using Frappe.IntegrationEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Frappe.Messaging.Inbox;

/// <summary>
/// Runs one message through a module handler exactly once per consumer: the inbox record and the handler's effect
/// are written in one transaction, and a message already in the inbox is skipped. A consumer's before-claim step
/// (<see cref="IBeforeClaimHandler{TEvent}"/>) runs first, outside that transaction. Acknowledging the broker is the
/// caller's job, and only after this returns.
/// </summary>
public sealed partial class InboxConsumer<TContext, TEvent>(
    IServiceScopeFactory scopes,
    string consumerName,
    ILogger<InboxConsumer<TContext, TEvent>> logger,
    TimeProvider? time = null)
    where TContext : DbContext
    where TEvent : IIntegrationEvent
{
    /// <summary>
    /// Upper bound, in seconds, for the after-commit steps; independent of the handler's deadline, which must not cut
    /// them. A constant rather than a static field, so it is not one copy per closed consumer type.
    /// </summary>
    private const int AfterCommitTimeoutSeconds = 10;

    public string ConsumerName { get; } = consumerName;

    public async Task<ConsumeResult> ConsumeAsync(IncomingMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        using var activity = StartActivity(message);

        TEvent integrationEvent;
        try
        {
            integrationEvent = JsonSerializer.Deserialize<TEvent>(message.Data.Span, JsonSerializerOptions.Web)
                ?? throw new JsonException($"Message {message.Id} has an empty payload.");
        }
        catch (JsonException unreadable)
        {
            activity?.SetStatus(ActivityStatusCode.Error, unreadable.Message);
            activity?.SetTag(MessagingMetrics.OutcomeTag, MessagingMetrics.OutcomeTagOf(ConsumedOutcome.DeadLettered));
            return new ConsumeResult(ConsumeOutcome.Rejected, $"{nameof(JsonException)}: {unreadable.Message}");
        }

        return await HandleOnceAsync(message, integrationEvent, activity, cancellationToken);
    }

    /// <summary>The consumer span of the message, a child of the trace it carries.</summary>
    private Activity? StartActivity(IncomingMessage message)
    {
        var activity = MessagingTelemetry.StartChildOf(
            $"process {message.Type}", ActivityKind.Consumer, message.TraceParent, message.TraceState);
        activity?.SetTag("messaging.system", "nats");
        activity?.SetTag("messaging.consumer.group.name", ConsumerName);
        activity?.SetTag("messaging.message.id", message.Id.ToString());
        return activity;
    }

    /// <summary>
    /// Runs the consumer's before-claim step, if any, outside any transaction, then claims the message and runs the handler
    /// in the claim's transaction; a claimed message is a duplicate.
    /// </summary>
    private async Task<ConsumeResult> HandleOnceAsync(
        IncomingMessage message, TEvent integrationEvent, Activity? activity, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        if (scope.ServiceProvider.GetService<CurrentMessage>() is { } current)
        {
            current.Id = message.Id;
            current.Consumer = ConsumerName;
        }

        if (BeforeClaim(scope.ServiceProvider) is { } early)
        {
            // Read before the slow step, in its own short statement: a message already handled must not call the other
            // system again. A delivery racing this one past the read is caught by the claim below.
            if (await IsClaimedAsync(db, message.Id, cancellationToken))
            {
                return await DuplicateAsync(scope.ServiceProvider, integrationEvent, message.Id, activity);
            }

            try
            {
                await early.HandleAsync(integrationEvent, cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                return Failed(message, activity, exception);
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var claimed = await db.Database.ExecuteSqlRawAsync(ClaimSql(db), [ConsumerName, message.Id], cancellationToken);
        if (claimed == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return await DuplicateAsync(scope.ServiceProvider, integrationEvent, message.Id, activity);
        }

        try
        {
            if (Handler(scope.ServiceProvider) is { } handler)
            {
                await handler.HandleAsync(integrationEvent, cancellationToken);
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            activity?.SetTag(MessagingMetrics.OutcomeTag, MessagingMetrics.OutcomeTagOf(ConsumedOutcome.Handled));
            await AfterCommitAsync(scope.ServiceProvider, integrationEvent, message.Id);
            return new ConsumeResult(ConsumeOutcome.Processed);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return Failed(message, activity, exception);
        }
    }

    /// <summary>
    /// A message this consumer already claimed: nothing runs but the after-commit steps. The first delivery may have
    /// committed but lost them (process crash, deadline); they are idempotent, so running them again recovers it.
    /// </summary>
    private async Task<ConsumeResult> DuplicateAsync(IServiceProvider services, TEvent integrationEvent, Guid messageId, Activity? activity)
    {
        activity?.SetTag(MessagingMetrics.OutcomeTag, MessagingMetrics.OutcomeTagOf(ConsumedOutcome.Duplicate));
        await AfterCommitAsync(services, integrationEvent, messageId);
        return new ConsumeResult(ConsumeOutcome.Duplicate);
    }

    private ConsumeResult Failed(IncomingMessage message, Activity? activity, Exception exception)
    {
        activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity?.SetTag(MessagingMetrics.OutcomeTag, MessagingMetrics.OutcomeTagOf(ConsumedOutcome.Failed));
        LogHandlerFailed(logger, exception, ConsumerName, message.Id, message.Type);
        return new ConsumeResult(ConsumeOutcome.Failed, $"{exception.GetType().Name}: {exception.Message}");
    }

    /// <summary>Whether this consumer's inbox already holds the message, read by its key outside any transaction.</summary>
    private Task<bool> IsClaimedAsync(TContext db, Guid messageId, CancellationToken cancellationToken) =>
        db.Set<InboxMessage>().AnyAsync(m => m.Consumer == ConsumerName && m.MessageId == messageId, cancellationToken);

    /// <summary>The step this consumer runs before the claim, outside any transaction: only one registered under its name.</summary>
    private IBeforeClaimHandler<TEvent>? BeforeClaim(IServiceProvider services) =>
        services.GetKeyedService<IBeforeClaimHandler<TEvent>>(ConsumerName);

    /// <summary>
    /// The handler this consumer runs in its transaction: its own, registered under its name; none when it has only
    /// after-commit or before-claim steps of its own; otherwise the event's single unnamed handler, which must exist (a
    /// consumer with no step at all fails the message rather than acknowledging it unhandled).
    /// </summary>
    private IIntegrationEventHandler<TEvent>? Handler(IServiceProvider services)
    {
        if (services.GetKeyedService<IIntegrationEventHandler<TEvent>>(ConsumerName) is { } own)
        {
            return own;
        }

        return OwnsSteps(services) ? null : services.GetRequiredService<IIntegrationEventHandler<TEvent>>();
    }

    /// <summary>Whether this consumer registered steps under its own name, so it runs those and never the event's unnamed ones.</summary>
    private bool OwnsSteps(IServiceProvider services) =>
        services.GetService<IServiceProviderIsKeyedService>() is { } keyed
        && (keyed.IsKeyedService(typeof(IIntegrationEventHandler<TEvent>), ConsumerName)
            || keyed.IsKeyedService(typeof(IAfterCommitHandler<TEvent>), ConsumerName)
            || keyed.IsKeyedService(typeof(IBeforeClaimHandler<TEvent>), ConsumerName));

    /// <summary>Best-effort effects that must only happen once the data is committed and visible (cache invalidation).</summary>
    private async Task AfterCommitAsync(IServiceProvider services, TEvent integrationEvent, Guid messageId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(AfterCommitTimeoutSeconds), time ?? TimeProvider.System);

        // Building the steps is part of the best effort too: the message is committed, so a step that cannot be built
        // is logged, never thrown back into a transaction that has already ended. The container builds them all at
        // once, so one that cannot be built skips every step of this message.
        IEnumerable<IAfterCommitHandler<TEvent>> hooks;
        try
        {
            hooks = OwnsSteps(services)
                ? services.GetKeyedServices<IAfterCommitHandler<TEvent>>(ConsumerName)
                : services.GetServices<IAfterCommitHandler<TEvent>>();
        }
        catch (Exception exception)
        {
            LogAfterCommitFailed(logger, exception, ConsumerName, messageId, nameof(IAfterCommitHandler<TEvent>));
            return;
        }

        foreach (var hook in hooks)
        {
            try
            {
                await hook.HandleAsync(integrationEvent, timeout.Token);
            }
            catch (Exception exception)
            {
                LogAfterCommitFailed(logger, exception, ConsumerName, messageId, hook.GetType().Name);
            }
        }
    }

    /// <summary>Claims the message for this consumer; concurrent duplicates wait on the key and then see a conflict.</summary>
    private static string ClaimSql(DbContext db)
    {
        var table = db.Model.FindEntityType(typeof(InboxMessage))
            ?? throw new InvalidOperationException($"{typeof(TContext).Name} has no inbox; call modelBuilder.AddInbox().");
        var name = $"\"{table.GetSchema()}\".\"{table.GetTableName()}\"";
        return "INSERT INTO " + name + " (consumer, message_id, received_at) VALUES ({0}, {1}, now()) ON CONFLICT DO NOTHING";
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Consumer {Consumer} committed message {MessageId} but after-commit step {Step} failed")]
    private static partial void LogAfterCommitFailed(ILogger logger, Exception exception, string consumer, Guid messageId, string step);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Consumer {Consumer} failed to handle message {MessageId} ({Type}); it will be retried")]
    private static partial void LogHandlerFailed(ILogger logger, Exception exception, string consumer, Guid messageId, string type);
}
