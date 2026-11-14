using NATS.Client.JetStream.Models;

namespace Frappe.Messaging.JetStream;

/// <summary>
/// A durable consumer's delivery, backoff and dead-letter settings, bound from <see cref="Section"/> (for example
/// <c>Messaging:Consumers:AckWait</c>) with today's values as defaults, and validated at startup by
/// <see cref="JetStreamConsumerOptionsValidator"/>.
/// </summary>
public sealed class JetStreamConsumerOptions
{
    /// <summary>The configuration section the hosts bind.</summary>
    public const string Section = "Messaging:Consumers";

    /// <summary>
    /// Handler failures before a message is moved to the dead-letter stream. Redeliveries caused by an outage
    /// (database or broker unavailable) do not count: an outage must never dead-letter healthy messages.
    /// </summary>
    public int MaxDeliveries { get; set; } = 6;

    /// <summary>
    /// Wait after a failed attempt and before the next one, by attempt number; the last value repeats. A configured
    /// list (<c>Messaging:Consumers:Backoff:0</c>, <c>:1</c>, ...) replaces this one whole.
    /// </summary>
    public IReadOnlyList<TimeSpan> Backoff { get; set; } =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10)];

    /// <summary>How long JetStream waits for an ack before redelivering on its own. At least one second.</summary>
    public TimeSpan AckWait { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Messages pulled ahead of processing. One by default: prefetched messages wait while earlier ones are handled
    /// and can outlive AckWait, causing pointless redeliveries. Raise it only for fast handlers.
    /// </summary>
    public int PrefetchCount { get; set; } = 1;

    /// <summary>
    /// Longest a handler is kept alive with progress signals. After it, the heartbeat stops and JetStream redelivers
    /// after AckWait, and the handler is cancelled so its transaction rolls back and releases its locks.
    /// </summary>
    public TimeSpan MaxHandlerDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Where a newly created durable consumer starts. <c>All</c> by default: a new projection replays the retained
    /// history and builds itself. JetStream refuses to change it on an existing consumer, so changing it later
    /// means a new consumer name (and a full replay into the inbox, which skips what was already handled).
    /// </summary>
    public ConsumerConfigDeliverPolicy DeliverFrom { get; set; } = ConsumerConfigDeliverPolicy.All;
}
