namespace Frappe.Notifications.Domain.Suppressions;

/// <summary>Where a suppression came from.</summary>
public enum SuppressionSource
{
    /// <summary>A provider event received by the webhook.</summary>
    Webhook,

    /// <summary>An operator added it.</summary>
    Manual,
}
