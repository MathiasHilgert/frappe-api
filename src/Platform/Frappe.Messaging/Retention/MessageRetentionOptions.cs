using Frappe.Messaging.JetStream;

namespace Frappe.Messaging.Retention;

/// <summary>
/// How long a module keeps its published outbox rows and its inbox records before the purge deletes them, bound from
/// <see cref="Section"/> (<c>Messaging:Retention:OutboxDays</c>, <c>InboxDays</c>, <c>BatchSize</c>) and validated at
/// startup by <see cref="MessageRetentionOptionsValidator"/>.
/// </summary>
public sealed class MessageRetentionOptions
{
    /// <summary>The configuration section the hosts bind.</summary>
    public const string Section = "Messaging:Retention";

    /// <summary>The longest retention accepted, in days: ten years.</summary>
    public const int MaxDays = 3650;

    /// <summary>Whole days after which a published outbox row is deleted; see <see cref="OutboxRetention"/>.</summary>
    public int OutboxDays { get; set; } = 7;

    /// <summary>Whole days after which an inbox record is deleted; see <see cref="InboxRetention"/>.</summary>
    public int InboxDays { get; set; } = 14;

    /// <summary>Most rows one delete statement removes, so no statement holds its locks for long.</summary>
    public int BatchSize { get; set; } = 1000;

    /// <summary>
    /// Age, measured from <c>published_at</c>, after which a published outbox row is deleted. Unpublished rows, parked
    /// ones included, are never deleted: they are the only copy of an event the broker has not stored.
    /// </summary>
    public TimeSpan OutboxRetention => TimeSpan.FromDays(OutboxDays);

    /// <summary>
    /// Age, measured from <c>received_at</c>, after which an inbox record is deleted. Consumers deliver from the start
    /// of the stream, so a record must outlive every message the stream still holds
    /// (<see cref="JetStreamTopology.ModuleRetention"/>), or a replay would run a handler twice.
    /// </summary>
    public TimeSpan InboxRetention => TimeSpan.FromDays(InboxDays);
}
