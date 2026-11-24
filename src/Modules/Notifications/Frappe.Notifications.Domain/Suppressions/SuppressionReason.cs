namespace Frappe.Notifications.Domain.Suppressions;

/// <summary>Why an address stopped receiving notifications.</summary>
public enum SuppressionReason
{
    /// <summary>The address bounced permanently.</summary>
    HardBounce,

    /// <summary>The recipient complained.</summary>
    Complaint,

    /// <summary>The provider suppressed the address itself.</summary>
    ProviderSuppression,
}
