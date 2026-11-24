using System.Diagnostics;

namespace Frappe.Notifications.Domain.Deliveries;

/// <summary>What each <see cref="NotificationCategory"/> permits (S9).</summary>
public static class NotificationCategoryExtensions
{
    extension(NotificationCategory category)
    {
        /// <summary>Whether a person may turn the category off: only marketing.</summary>
        public bool CanOptOut => category switch
        {
            NotificationCategory.Security => false,
            NotificationCategory.Transactional => false,
            NotificationCategory.Marketing => true,
            _ => throw new UnreachableException($"Unknown category {category}."),
        };

        /// <summary>Whether a notification of the category may carry links: everything but security.</summary>
        public bool AllowsLinks => category switch
        {
            NotificationCategory.Security => false,
            NotificationCategory.Transactional => true,
            NotificationCategory.Marketing => true,
            _ => throw new UnreachableException($"Unknown category {category}."),
        };
    }
}
