using Frappe.Messaging.Outbox;
using NodaTime;

namespace Frappe.Messaging;

/// <summary>
/// An integration event waiting to be published, stored in the module's own schema in the same transaction as
/// the change that produced it. Its fields are the CloudEvents attributes it will be published with.
/// </summary>
public sealed class OutboxMessage
{
    private OutboxMessage()
    {
        Type = null!;
        Source = null!;
        DataContentType = null!;
        Data = null!;
    }

    internal OutboxMessage(Guid id, string type, string source, string data, Instant occurredAt, string? traceParent, string? traceState)
    {
        Id = id;
        Type = type;
        Source = source;
        DataContentType = "application/json";
        Data = data;
        OccurredAt = occurredAt;
        TraceParent = traceParent;
        TraceState = traceState;
    }

    /// <summary>CloudEvents <c>id</c>; also the JetStream message id and the consumers' inbox key.</summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Insertion order, assigned by the database when the row is inserted; the relay publishes in this order.
    /// It is best effort, not a global order: concurrent transactions can commit out of sequence, and events of the
    /// same save have no order among themselves. Consumers must not depend on cross-aggregate ordering.
    /// </summary>
    public long Sequence { get; private set; }

    /// <summary>CloudEvents <c>type</c>, for example <c>frappe.geo.holidays_synced.v1</c>.</summary>
    public string Type { get; private set; }

    /// <summary>CloudEvents <c>source</c>, the producing module, for example <c>/frappe/geo</c>.</summary>
    public string Source { get; private set; }

    public string DataContentType { get; private set; }

    /// <summary>The event payload as JSON.</summary>
    public string Data { get; private set; }

    /// <summary>CloudEvents <c>time</c>: when the change was saved.</summary>
    public Instant OccurredAt { get; private set; }

    /// <summary>W3C trace context of the operation that saved the change, restored by the relay when publishing.</summary>
    public string? TraceParent { get; private set; }

    public string? TraceState { get; private set; }

    public Instant? PublishedAt { get; private set; }

    /// <summary>
    /// While set and in the future, a relay owns this row and is publishing it outside any transaction. If that relay
    /// dies, the lease expires and another relay takes the row (the broker and the inbox deduplicate the repeat).
    /// </summary>
    public Instant? LeasedUntil { get; private set; }

    public int Attempts { get; private set; }

    /// <summary>
    /// Earliest moment the relay may try again after a failure, so one failing message never blocks the ones behind
    /// it. Null when the message has never failed, and also when it is parked after its last allowed attempt.
    /// </summary>
    public Instant? NextAttemptAt { get; private set; }

    public string? LastError { get; private set; }

    internal void MarkPublished(Instant publishedAt)
    {
        PublishedAt = publishedAt;
        LastError = null;
        LeasedUntil = null;
    }

    /// <summary>Gives the row back without counting an attempt (broker unavailable, pass budget spent).</summary>
    internal void ReleaseLease() => LeasedUntil = null;

    internal void RecordFailure(string error, Instant? nextAttemptAt)
    {
        LeasedUntil = null;
        Attempts++;
        NextAttemptAt = nextAttemptAt;
        LastError = error.Length <= OutboxModel.MaxErrorLength ? error : error[..OutboxModel.MaxErrorLength];
    }
}
