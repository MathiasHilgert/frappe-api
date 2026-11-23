namespace Frappe.Messaging;

/// <summary>How a consumer settled one delivery with the broker: every delivery counts once, by the one that applied.</summary>
public enum ConsumedOutcome
{
    /// <summary>The handler's effect committed and the delivery was acknowledged.</summary>
    Handled,

    /// <summary>This consumer had already handled the message; it was acknowledged without running the handler.</summary>
    Duplicate,

    /// <summary>The delivery was not settled (handler failure, timeout, database or broker error) and will be redelivered.</summary>
    Failed,

    /// <summary>The message was moved to the dead-letter stream: unreadable, or out of handler attempts.</summary>
    DeadLettered,
}
