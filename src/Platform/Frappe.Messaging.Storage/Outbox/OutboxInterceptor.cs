using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Frappe.Aggregates;
using Frappe.IntegrationEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NodaTime;

namespace Frappe.Messaging.Storage.Outbox;

/// <summary>
/// Right before a save, takes the pending domain events of every tracked aggregate, translates them and adds them
/// to the outbox. They are inserted by the same <c>SaveChanges</c>, so they commit or roll back with the change.
/// If a save fails, its outbox rows stay tracked by that context; a retry must reload the aggregate in a new
/// context (the events were already taken from the instance), which is what a re-run of the use case does.
/// The module's unit of work has already handed the same events to its in-process handlers (read models) before the
/// save, without taking them, so both reach this one <c>SaveChanges</c>.
/// </summary>
internal sealed class OutboxInterceptor(IIntegrationEventTranslator translator, string source, IClock clock) : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

    /// <summary>The translator this interceptor turns domain events with.</summary>
    internal IIntegrationEventTranslator Translator => translator;

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Enqueue(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Enqueue(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Enqueue(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var aggregates = context.ChangeTracker.Entries<AggregateRoot>()
            .Select(entry => entry.Entity)
            .Where(aggregate => aggregate.DomainEvents.Count > 0)
            .ToArray();
        if (aggregates.Length == 0)
        {
            return;
        }

        // Translate and serialize first: if anything throws, the events are still on their aggregates.
        var occurredAt = clock.GetCurrentInstant();
        var (traceParent, traceState) = CurrentTraceContext();
        var messages = aggregates
            .SelectMany(aggregate => aggregate.DomainEvents)
            .SelectMany(translator.Translate)
            .Select(integrationEvent => new OutboxMessage(
                Guid.CreateVersion7(occurredAt.ToDateTimeOffset()),
                TypeOf(integrationEvent),
                source,
                JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType(), Json),
                occurredAt,
                traceParent,
                traceState))
            .ToArray();

        foreach (var aggregate in aggregates)
        {
            aggregate.DequeueDomainEvents();
        }

        context.AddRange(messages);
    }

    /// <summary>
    /// The W3C context of the current operation. Telemetry must never fail a business save: a non-W3C id is not a
    /// traceparent, and a tracestate longer than the stored limit (W3C asks vendors to keep at least 512 characters)
    /// is dropped rather than truncated, because a cut tracestate is invalid.
    /// </summary>
    private static (string? TraceParent, string? TraceState) CurrentTraceContext()
    {
        if (Activity.Current is not { IdFormat: ActivityIdFormat.W3C } current)
        {
            return (null, null);
        }

        var traceState = current.TraceStateString is { Length: <= OutboxModel.MaxTraceStateLength } state ? state : null;
        return (current.Id, traceState);
    }

    private static string TypeOf(IIntegrationEvent integrationEvent) =>
        integrationEvent.GetType().GetCustomAttribute<IntegrationEventAttribute>()?.Type
        ?? throw new InvalidOperationException(
            $"Integration event '{integrationEvent.GetType().Name}' has no [IntegrationEvent(\"<type>\")] attribute.");
}
