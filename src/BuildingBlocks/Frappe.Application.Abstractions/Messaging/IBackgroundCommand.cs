namespace Frappe.Application.Abstractions.Messaging;

/// <summary>
/// A command no caller awaits: another handler cascades it (e.g. a reset code issued after the request answered) or a
/// recurring job publishes it, and nobody reads an answer. Its handler returns only
/// its cascading messages (<c>OutgoingMessages</c>) or nothing, never a <c>Result</c>, because a published handler's
/// return value is itself cascaded as a message and a <c>Result</c> has nowhere to go.
/// </summary>
public interface IBackgroundCommand : ICommand;
