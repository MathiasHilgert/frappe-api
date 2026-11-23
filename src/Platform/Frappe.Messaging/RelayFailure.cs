namespace Frappe.Messaging;

/// <summary>How the relay failed to publish one outbox message.</summary>
public enum RelayFailure
{
    /// <summary>The broker rejected the message; it is retried after a backoff.</summary>
    Rejected,

    /// <summary>The broker rejected the message on its last allowed attempt; it left the pending rows for good.</summary>
    Parked,

    /// <summary>The broker was unavailable; the row is untouched and the next pass retries.</summary>
    Unavailable,
}
