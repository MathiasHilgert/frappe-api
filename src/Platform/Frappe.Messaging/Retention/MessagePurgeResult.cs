namespace Frappe.Messaging.Retention;

/// <summary>Rows one purge deleted from a module's outbox and inbox.</summary>
public sealed record MessagePurgeResult(int OutboxRows, int InboxRows);
