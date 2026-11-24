namespace Frappe.Notifications.Domain.Deliveries;

/// <summary>What a provider says about a delivery, channel-neutral (the core seam): the stored form of a parked provider event.</summary>
public enum DeliveryFeedback
{
    /// <summary>The message reached the recipient.</summary>
    Delivered,

    /// <summary>The address will never accept the message.</summary>
    PermanentFailure,

    /// <summary>The recipient reported the message as unwanted.</summary>
    Complaint,

    /// <summary>The provider could not deliver yet and keeps trying.</summary>
    Delayed,

    /// <summary>The provider added the address to its own suppression list.</summary>
    SuppressedByProvider,
}
