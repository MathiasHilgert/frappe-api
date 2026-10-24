namespace Frappe.Application.Abstractions.Messaging;

/// <summary>
/// Marks a message that changes state and answers only success or failure: its handler returns <c>Result</c>.
/// The handler loads the aggregate, lets it decide, saves, and side effects leave as cascading messages through the
/// outbox, in the same transaction.
/// </summary>
public interface ICommand;

/// <summary>
/// Marks a message that changes state and answers with <typeparamref name="TResponse"/>: its handler returns
/// <c>Result&lt;TResponse&gt;</c>, carrying at most an id or a small outcome, never a read model.
/// </summary>
/// <typeparam name="TResponse">What a successful command answers with.</typeparam>
#pragma warning disable S2326 // The type parameter declares the answer; the architecture tests pin the handler to it.
public interface ICommand<TResponse> : ICommand;
#pragma warning restore S2326
