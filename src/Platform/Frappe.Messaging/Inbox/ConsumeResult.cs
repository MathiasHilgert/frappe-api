namespace Frappe.Messaging.Inbox;

/// <summary>The outcome of consuming one message, with the error text when it did not succeed.</summary>
public sealed record ConsumeResult(ConsumeOutcome Outcome, string? Error = null);
