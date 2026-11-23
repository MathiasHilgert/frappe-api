using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Diagnostics.Metrics;
using NodaTime;

namespace Frappe.Messaging;

/// <summary>
/// Instruments of the messaging platform on meter <c>Frappe.Messaging</c> (<see cref="MessagingTelemetry.Name"/>),
/// which hosts export through <c>Frappe.*</c>. Created once through <see cref="IMeterFactory"/>. Labels are module and
/// consumer names only, never message ids or types.
/// </summary>
public sealed partial class MessagingMetrics
{
    /// <summary>Counter: rows a retention purge deleted, by module and table (the message purge's and a module's own).</summary>
    public const string PurgedMetric = "frappe.messaging.purged";

    /// <summary>Counter: outbox messages the relay published to the broker, by module.</summary>
    public const string RelayedMetric = "frappe.messaging.relayed";

    /// <summary>
    /// Gauge: seconds since the oldest unpublished outbox message of a module occurred, as the module's relay last saw
    /// it; 0 when nothing is waiting. Parked messages count, so it stays high until an operator deals with them; an idle
    /// relay looks only once per <c>SampleInterval</c>, and the gauge ages what it saw in between.
    /// </summary>
    public const string OutboxOldestAgeMetric = "frappe.messaging.outbox.oldest_age";

    /// <summary>
    /// Gauge: outbox messages of a module parked after their last allowed attempt, as the module's relay last counted
    /// them. They are never retried or purged on their own: an operator requeues or deletes them.
    /// </summary>
    public const string OutboxParkedMetric = "frappe.messaging.outbox.parked";

    /// <summary>
    /// Counter: deliveries a consumer settled, by <see cref="ConsumerTag"/> and <see cref="OutcomeTag"/> (<c>handled</c>,
    /// <c>duplicate</c>, <c>failed</c> or <c>dead_lettered</c>).
    /// </summary>
    public const string ConsumedMetric = "frappe.messaging.consumed";

    /// <summary>
    /// Counter: outbox messages the relay could not publish, by module and <see cref="OutcomeTag"/> (<c>rejected</c>,
    /// <c>parked</c> or <c>unavailable</c>).
    /// </summary>
    public const string RelayFailuresMetric = "frappe.messaging.relay.failures";

    /// <summary>
    /// Counter: consumer failures that are not a handler failure, by <see cref="ConsumerTag"/> and <see cref="ReasonTag"/>
    /// (<c>subscription</c> or <c>after_commit</c>).
    /// </summary>
    public const string ConsumerFailuresMetric = "frappe.messaging.consumer.failures";

    /// <summary>Counter: retention purges that failed, by module; a purge that fails every run no longer looks idle.</summary>
    public const string PurgeFailuresMetric = "frappe.messaging.purge.failures";

    /// <summary>The reason a consumer failed: <c>subscription</c> or <c>after_commit</c>.</summary>
    public const string ReasonTag = "frappe.reason";

    /// <summary>The module label, for example <c>orders</c>.</summary>
    public const string ModuleTag = "frappe.module";

    /// <summary>The table label: <c>outbox_messages</c>, <c>inbox_messages</c>, or a module's own table such as <c>receipts</c>.</summary>
    public const string TableTag = "frappe.messaging.table";

    /// <summary>The durable consumer name, <c>&lt;module&gt;-&lt;purpose&gt;</c>, for example <c>orders-projection</c>.</summary>
    public const string ConsumerTag = "frappe.messaging.consumer";

    /// <summary>The outcome of what was counted, as in every module.</summary>
    public const string OutcomeTag = "frappe.outcome";

    private readonly Purged _purged;
    private readonly Relayed _relayed;
    private readonly Consumed _consumed;
    private readonly RelayFailures _relayFailures;
    private readonly ConsumerFailures _consumerFailures;
    private readonly PurgeFailures _purgeFailures;
    private readonly TimeProvider _time;

    /// <summary>Per module, the oldest unpublished message's time, or null when the relay found none.</summary>
    private readonly ConcurrentDictionary<string, Instant?> _oldestUnpublished = new(StringComparer.Ordinal);

    /// <summary>Per module, how many parked messages the relay last counted.</summary>
    private readonly ConcurrentDictionary<string, long> _parked = new(StringComparer.Ordinal);

    public MessagingMetrics(IMeterFactory meters, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(meters);
        _time = time ?? TimeProvider.System;
        var meter = meters.Create(MessagingTelemetry.Name);
        _purged = CreatePurged(meter);
        _relayed = CreateRelayed(meter);
        _consumed = CreateConsumed(meter);
        _relayFailures = CreateRelayFailures(meter);
        _consumerFailures = CreateConsumerFailures(meter);
        _purgeFailures = CreatePurgeFailures(meter);
        meter.CreateObservableGauge(
            OutboxOldestAgeMetric,
            ObserveOldestAge,
            unit: "s",
            description: "Seconds since the oldest unpublished outbox message occurred, by module; 0 when none is waiting.");
        meter.CreateObservableGauge(
            OutboxParkedMetric,
            ObserveParked,
            unit: "{message}",
            description: "Outbox messages parked after their last allowed attempt, by module; an operator requeues or deletes them.");
    }

    /// <summary><paramref name="rows"/> rows of <paramref name="module"/>'s <paramref name="table"/> were purged.</summary>
    public void Purged(string module, string table, long rows) =>
        _purged.Add(rows, new PurgedTags { Module = module, Table = table });

    /// <summary>The relay of <paramref name="module"/> published <paramref name="messages"/> messages.</summary>
    public void Relayed(string module, long messages)
    {
        if (messages > 0)
        {
            _relayed.Add(messages, new ModuleTags { Module = module });
        }
    }

    /// <summary>
    /// The relay of <paramref name="module"/> found <paramref name="oldest"/> as its oldest unpublished message's time
    /// (null: none waiting). The gauge ages it at every collection, so it keeps growing if the relay stops looking.
    /// </summary>
    public void OldestUnpublished(string module, Instant? oldest) => _oldestUnpublished[module] = oldest;

    /// <summary>The relay of <paramref name="module"/> counted <paramref name="messages"/> parked messages in its outbox.</summary>
    public void Parked(string module, long messages) => _parked[module] = messages;

    /// <summary><paramref name="consumer"/> settled one delivery with <paramref name="outcome"/>.</summary>
    public void Consumed(string consumer, ConsumedOutcome outcome) =>
        _consumed.Add(1, new ConsumedTags { Consumer = consumer, Outcome = OutcomeTagOf(outcome) });

    /// <summary>The relay of <paramref name="module"/> failed a message with <paramref name="outcome"/>.</summary>
    public void RelayFailed(string module, RelayFailure outcome) =>
        _relayFailures.Add(1, new RelayFailureTags { Module = module, Outcome = RelayFailureTagOf(outcome) });

    /// <summary><paramref name="consumer"/> failed for <paramref name="reason"/>.</summary>
    public void ConsumerFailed(string consumer, ConsumerFailure reason) =>
        _consumerFailures.Add(1, new ConsumerFailureTags { Consumer = consumer, Reason = ConsumerFailureTagOf(reason) });

    /// <summary>A retention purge of <paramref name="module"/> failed.</summary>
    public void PurgeFailed(string module) => _purgeFailures.Add(1, new ModuleTags { Module = module });

    /// <summary>The <see cref="OutcomeTag"/> value of a relay failure; a value without its own arm fails.</summary>
    public static string RelayFailureTagOf(RelayFailure outcome) => outcome switch
    {
        RelayFailure.Rejected => "rejected",
        RelayFailure.Parked => "parked",
        RelayFailure.Unavailable => "unavailable",
        _ => throw new UnreachableException($"Unknown relay failure {outcome}."),
    };

    /// <summary>The <see cref="ReasonTag"/> value of a consumer failure; a value without its own arm fails.</summary>
    public static string ConsumerFailureTagOf(ConsumerFailure reason) => reason switch
    {
        ConsumerFailure.Subscription => "subscription",
        ConsumerFailure.AfterCommit => "after_commit",
        _ => throw new UnreachableException($"Unknown consumer failure {reason}."),
    };

    /// <summary>The <see cref="OutcomeTag"/> value of <paramref name="outcome"/>; a value without its own arm fails.</summary>
    public static string OutcomeTagOf(ConsumedOutcome outcome) => outcome switch
    {
        ConsumedOutcome.Handled => "handled",
        ConsumedOutcome.Duplicate => "duplicate",
        ConsumedOutcome.Failed => "failed",
        ConsumedOutcome.DeadLettered => "dead_lettered",
        _ => throw new UnreachableException($"Unknown consumed outcome {outcome}."),
    };

    [Counter<long>(
        typeof(PurgedTags),
        Name = PurgedMetric,
        Unit = "{row}",
        Description = "Rows a retention purge deleted: published outbox rows, inbox records and a module's own records.")]
    public static partial Purged CreatePurged(Meter meter);

    [Counter<long>(
        typeof(ModuleTags),
        Name = RelayedMetric,
        Unit = "{message}",
        Description = "Outbox messages the relay published to the broker, by module.")]
    public static partial Relayed CreateRelayed(Meter meter);

    [Counter<long>(
        typeof(ConsumedTags),
        Name = ConsumedMetric,
        Unit = "{delivery}",
        Description = "Deliveries a consumer settled, by consumer and outcome (handled, duplicate, failed, dead_lettered).")]
    public static partial Consumed CreateConsumed(Meter meter);

    [Counter<long>(
        typeof(RelayFailureTags),
        Name = RelayFailuresMetric,
        Unit = "{message}",
        Description = "Outbox messages the relay could not publish, by module and outcome (rejected, parked, unavailable).")]
    public static partial RelayFailures CreateRelayFailures(Meter meter);

    [Counter<long>(
        typeof(ConsumerFailureTags),
        Name = ConsumerFailuresMetric,
        Unit = "{failure}",
        Description = "Consumer failures that are not a handler failure, by consumer and reason (subscription, after_commit).")]
    public static partial ConsumerFailures CreateConsumerFailures(Meter meter);

    [Counter<long>(
        typeof(ModuleTags),
        Name = PurgeFailuresMetric,
        Unit = "{purge}",
        Description = "Retention purges that failed, by module.")]
    public static partial PurgeFailures CreatePurgeFailures(Meter meter);

    private IEnumerable<Measurement<long>> ObserveParked() =>
        _parked.Select(parked => new Measurement<long>(parked.Value, new KeyValuePair<string, object?>(ModuleTag, parked.Key)));

    private IEnumerable<Measurement<double>> ObserveOldestAge()
    {
        var now = Instant.FromDateTimeOffset(_time.GetUtcNow());
        foreach (var (module, oldest) in _oldestUnpublished)
        {
            var age = oldest is { } since ? Math.Max(0, (now - since).TotalSeconds) : 0;
            yield return new Measurement<double>(age, new KeyValuePair<string, object?>(ModuleTag, module));
        }
    }
}
