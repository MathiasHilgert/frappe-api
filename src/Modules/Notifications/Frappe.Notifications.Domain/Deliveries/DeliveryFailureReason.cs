namespace Frappe.Notifications.Domain.Deliveries;

/// <summary>Why a delivery is <see cref="DeliveryStatus.Failed"/>: a code, never provider text (S13).</summary>
public enum DeliveryFailureReason
{
    /// <summary>The provider refused the request permanently (a validation 4xx, S14).</summary>
    ProviderRejected,

    /// <summary>The address cannot receive mail.</summary>
    InvalidRecipient,
}
