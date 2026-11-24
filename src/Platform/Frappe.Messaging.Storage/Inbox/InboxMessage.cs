using NodaTime;

// Exception to ARCH-15: EF names an entity by its CLR full name and every module snapshot records Frappe.Messaging.<Name>.
namespace Frappe.Messaging;

/// <summary>Proof that a consumer already handled a message: the durable half of at-least-once deduplication.</summary>
public sealed class InboxMessage
{
    private InboxMessage()
    {
        Consumer = null!;
    }

    /// <summary>Durable consumer name, <c>&lt;module&gt;-&lt;purpose&gt;</c>. Names are forever: renaming one re-consumes history.</summary>
    public string Consumer { get; private set; }

    /// <summary>CloudEvents <c>id</c> of the handled message.</summary>
    public Guid MessageId { get; private set; }

    public Instant ReceivedAt { get; private set; }
}
