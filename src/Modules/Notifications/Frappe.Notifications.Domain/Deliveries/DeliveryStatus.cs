namespace Frappe.Notifications.Domain.Deliveries;

/// <summary>
/// Where a delivery stands. <see cref="Sent"/> and the statuses the provider reports afterwards need a provider message id and a
/// sent instant; <see cref="Failed"/> needs a <see cref="DeliveryFailureReason"/>; <see cref="Unresolved"/> is a recipient
/// reference no directory could resolve (S3), never retried.
/// </summary>
public enum DeliveryStatus
{
    /// <summary>Handed to the provider.</summary>
    Sent,

    /// <summary>The provider reports it was delivered.</summary>
    Delivered,

    /// <summary>The provider reports a delay and keeps trying.</summary>
    Delayed,

    /// <summary>The provider reports a permanent bounce.</summary>
    Bounced,

    /// <summary>The recipient complained.</summary>
    Complained,

    /// <summary>Permanently not sent; the reason says why.</summary>
    Failed,

    /// <summary>The recipient reference did not resolve to a contact.</summary>
    Unresolved,
}
