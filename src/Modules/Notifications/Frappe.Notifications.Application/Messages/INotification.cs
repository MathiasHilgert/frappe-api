using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.Application.Messages;

// Static abstract members declare a kind's contract: one class, one kind, so S2743 (statics in generic types) does not apply.
#pragma warning disable S2743
/// <summary>
/// One kind of notification: a class that names itself, says what it is for, gives a sample of its payload and says who it is for. It
/// knows nothing of how it is rendered or sent: a channel plug-in adds its own interface on top (<c>IEmailNotification&lt;TPayload&gt;</c>
/// and so on), so the payload type is declared once and the core stays channel-neutral (S0).
/// </summary>
/// <typeparam name="TPayload">The data the notification needs; it holds no secret that must not travel the outbox (S13).</typeparam>
public interface INotification<TPayload>
    where TPayload : notnull
{
    /// <summary>The kind's stable name, lower snake case (<c>account_activation_code</c>); it is stored, so it never changes.</summary>
    static abstract string Kind { get; }

    /// <summary>What the notification is for; it decides whether a person may turn it off (S9).</summary>
    static abstract NotificationCategory Category { get; }

    /// <summary>A payload that renders every language of the kind; tests, golden files and examples use it.</summary>
    static abstract TPayload Sample { get; }

    /// <summary>Who the notification is for, taken from its payload.</summary>
    Recipient RecipientOf(TPayload payload);
}
