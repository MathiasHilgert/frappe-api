using System.ComponentModel.DataAnnotations;
using NATS.Client.JetStream.Models;

namespace Frappe.Messaging.JetStream;

/// <summary>
/// A durable consumer's delivery, backoff and dead-letter settings, bound from <see cref="Section"/> (for example
/// <c>Messaging:Consumers:AckWait</c>) with today's values as defaults, and validated at startup by
/// <see cref="JetStreamConsumerOptionsValidator"/>.
/// </summary>
public sealed class JetStreamConsumerOptions : IValidatableObject
{
    /// <summary>The configuration section the hosts bind.</summary>
    public const string Section = "Messaging:Consumers";

    /// <summary>The section the options were bound from, so a rejection names the consumer's real key.</summary>
    internal string ConfigurationSection { get; set; } = Section;

    /// <summary>
    /// <c>Messaging:Consumers:MaxDeliveries</c>: handler failures before a message is moved to the dead-letter
    /// stream. Default 6. Range 1 or more. Redeliveries caused by an outage (database or broker unavailable) do not
    /// count: an outage never dead-letters healthy messages. Raising it retries a poison message longer before it
    /// is parked; lowering it parks it sooner.
    /// </summary>
    public int MaxDeliveries { get; set; } = 6;

    /// <summary>
    /// <c>Messaging:Consumers:Backoff</c>: the wait after a failed attempt and before the next one, by attempt
    /// number; the last value repeats. Default 1 s, 5 s, 30 s, 2 min, 10 min. Range a non-empty list of positive
    /// time spans. A configured list (<c>Messaging:Consumers:Backoff:0</c>, <c>:1</c>, ...) replaces the default
    /// whole. Longer waits spare a struggling dependency and delay recovery.
    /// </summary>
    public IReadOnlyList<TimeSpan> Backoff { get; set; } =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10)];

    /// <summary>
    /// <c>Messaging:Consumers:AckWait</c>: how long JetStream waits for an ack before it redelivers on its own.
    /// Default 30 s. Range 1 s or more, and at most <see cref="MaxHandlerDuration"/>. Raising it slows the
    /// redelivery of a message whose consumer died; lowering it risks redelivering one that is still being handled.
    /// </summary>
    public TimeSpan AckWait { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// <c>Messaging:Consumers:PrefetchCount</c>: messages pulled ahead of processing. Default 1. Range 1 or more.
    /// Prefetched messages wait while earlier ones are handled and can outlive <see cref="AckWait"/>, causing
    /// pointless redeliveries, so raise it only for fast handlers; it multiplies the memory held per consumer.
    /// </summary>
    public int PrefetchCount { get; set; } = 1;

    /// <summary>
    /// <c>Messaging:Consumers:MaxHandlerDuration</c>: the longest a handler is kept alive with progress signals.
    /// Default 5 minutes. Range at least <see cref="AckWait"/> (and, for a consumer that calls an external system, at least its call
    /// budget). After it the heartbeat stops, JetStream redelivers after <see cref="AckWait"/> and the handler is
    /// cancelled so its transaction rolls back and releases its locks. Raising it lets slow handlers finish and
    /// holds their locks longer.
    /// </summary>
    public TimeSpan MaxHandlerDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// <c>Messaging:Consumers:DeliverFrom</c>: where a newly created durable consumer starts. Default <c>All</c>: a
    /// new projection reads the retained history and builds itself. Range any <c>ConsumerConfigDeliverPolicy</c>.
    /// JetStream refuses to change it on an existing consumer, so changing it later means a new consumer name (and
    /// a full redelivery into the inbox, which skips what was already handled).
    /// </summary>
    public ConsumerConfigDeliverPolicy DeliverFrom { get; set; } = ConsumerConfigDeliverPolicy.All;

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (AckWait < TimeSpan.FromSeconds(1))
        {
            yield return Rejected(nameof(AckWait), $"must be at least 1 s, not '{AckWait}'.");
        }

        if (MaxDeliveries < 1)
        {
            yield return Rejected(nameof(MaxDeliveries), $"must be at least 1, not '{MaxDeliveries}'.");
        }

        if (PrefetchCount < 1)
        {
            yield return Rejected(nameof(PrefetchCount), $"must be at least 1, not '{PrefetchCount}'.");
        }

        if (Backoff.Count == 0)
        {
            yield return Rejected(nameof(Backoff), "must not be empty.");
        }

        if (MaxHandlerDuration < AckWait)
        {
            yield return Rejected(nameof(MaxHandlerDuration), $"must be at least AckWait, not '{MaxHandlerDuration}'.");
        }
    }

    private ValidationResult Rejected(string key, string reason) =>
        new($"Configuration '{ConfigurationSection}:{key}' {reason}", [key]);
}
