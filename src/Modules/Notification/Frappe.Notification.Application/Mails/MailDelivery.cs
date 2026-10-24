namespace Frappe.Notification.Application.Mails;

/// <summary>How a delivery is identified to the provider, so a retried send is recognised as the same mail.</summary>
public static class MailDelivery
{
    /// <summary>The idempotency key of the delivery triggered by message <paramref name="messageId"/>.</summary>
    public static string KeyFor(Guid messageId) => $"mail-{messageId:N}";
}
