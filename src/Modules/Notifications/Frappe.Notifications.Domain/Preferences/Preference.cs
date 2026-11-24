using Frappe.Aggregates;
using Frappe.Notifications.Domain.Deliveries;
using Frappe.Results;
using NodaTime;

// EF Core materializes the entity through its private constructor and setters.
#pragma warning disable S1144, S3453
namespace Frappe.Notifications.Domain.Preferences;

/// <summary>
/// A recipient's choice to stop one category. Only <see cref="NotificationCategory.Marketing"/> can be turned off (S9); preferences never
/// expire (S11).
/// </summary>
public sealed class Preference : AggregateRoot
{
    private Preference() => RecipientKey = null!;

    private Preference(RecipientKey recipientKey, NotificationCategory category, Instant now)
    {
        RecipientKey = recipientKey.Value;
        Category = category;
        OptedOutAt = now;
        Raise(new PreferenceOptedOut(category));
    }

    public string RecipientKey { get; private set; }

    public NotificationCategory Category { get; private set; }

    public Instant OptedOutAt { get; private set; }

    /// <summary>A recipient's choice to stop <paramref name="category"/>; refused when the category cannot be turned off (S9).</summary>
    public static Result<Preference> OptOut(RecipientKey recipientKey, NotificationCategory category, Instant now)
    {
        ArgumentNullException.ThrowIfNull(recipientKey);
        if (!category.CanOptOut)
        {
            return Error.RuleViolation("notifications.preference.category_cannot_opt_out", ("category", category.ToString()));
        }

        return new Preference(recipientKey, category, now);
    }
}
