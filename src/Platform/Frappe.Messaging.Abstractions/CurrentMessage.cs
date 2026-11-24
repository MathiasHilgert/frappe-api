namespace Frappe.Messaging.Abstractions;

/// <summary>
/// The message the consumer of this scope is handling, for a handler that needs more than the event: its CloudEvents
/// <see cref="Id"/>, stable across redeliveries (the outbox row's id), is a natural idempotency key for an external call
/// (an external provider), and <see cref="Consumer"/> names the inbox it is recorded in. A module that wants it registers it
/// scoped; the consumer of the Worker fills it before the handler runs. <see langword="null"/> outside a consumer.
/// </summary>
public sealed class CurrentMessage
{
    /// <summary>The CloudEvents <c>id</c> of the message being handled.</summary>
    public Guid? Id { get; internal set; }

    /// <summary>The durable name of the consumer handling it, the first half of its inbox key.</summary>
    public string? Consumer { get; internal set; }
}
