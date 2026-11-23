namespace Frappe.Messaging.Inbox;

/// <summary>What happened to one delivered message.</summary>
public enum ConsumeOutcome
{
    /// <summary>The handler ran and its effect committed together with the inbox record.</summary>
    Processed,

    /// <summary>This consumer had already handled the message; nothing ran.</summary>
    Duplicate,

    /// <summary>The handler failed; its effect and the inbox record were rolled back, so it can be retried.</summary>
    Failed,

    /// <summary>The payload cannot be read as the expected event; retrying cannot help.</summary>
    Rejected,
}
