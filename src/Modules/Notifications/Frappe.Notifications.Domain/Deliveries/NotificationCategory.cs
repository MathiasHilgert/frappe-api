namespace Frappe.Notifications.Domain.Deliveries;

/// <summary>What a notification is for; it decides whether a person may turn it off (S9). Stored as a snapshot at send time (DATA-12).</summary>
public enum NotificationCategory
{
    /// <summary>Account security (codes, alerts): cannot be turned off, carries no links.</summary>
    Security,

    /// <summary>A consequence of something the recipient did: cannot be turned off.</summary>
    Transactional,

    /// <summary>Announcements and campaigns: the only category a person may turn off.</summary>
    Marketing,
}
