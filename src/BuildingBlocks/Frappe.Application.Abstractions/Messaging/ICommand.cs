namespace Frappe.Application.Abstractions.Messaging;

/// <summary>
/// Marks a message that changes state. Its handler loads the aggregate, lets it decide, saves, and returns a
/// <c>Result</c> carrying at most an id or a small outcome, never a read model. Side effects leave as cascading
/// messages through the outbox, in the same transaction.
/// </summary>
public interface ICommand;
