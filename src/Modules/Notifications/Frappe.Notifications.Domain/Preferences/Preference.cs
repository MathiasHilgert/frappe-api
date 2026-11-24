using Frappe.Notifications.Domain.Deliveries;
using NodaTime;

// EF Core materializes the entity through its private constructor and setters; behavior arrives with the preferences.
#pragma warning disable S1144, S3453
namespace Frappe.Notifications.Domain.Preferences;

/// <summary>
/// A recipient's choice to stop one category. Only <see cref="NotificationCategory.Marketing"/> can be turned off (S9); preferences never
/// expire (S11).
/// </summary>
public sealed class Preference
{
    private Preference() => RecipientKey = null!;

    public string RecipientKey { get; private set; }

    public NotificationCategory Category { get; private set; }

    public Instant OptedOutAt { get; private set; }
}
