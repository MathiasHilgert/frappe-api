namespace Frappe.Application.Abstractions.Messaging;

/// <summary>
/// A command nobody waits for: a recurring job publishes it and no caller reads an answer. Its handler returns only
/// its cascading messages (<c>OutgoingMessages</c>) or nothing, never a <c>Result</c>, because a published handler's
/// return value is itself cascaded as a message and a <c>Result</c> has nowhere to go.
/// </summary>
public interface IBackgroundCommand : ICommand;
