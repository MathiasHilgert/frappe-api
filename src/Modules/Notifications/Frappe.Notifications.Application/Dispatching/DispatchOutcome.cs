namespace Frappe.Notifications.Application.Dispatching;

/// <summary>What became of one intended delivery.</summary>
public enum DispatchOutcome
{
    /// <summary>The provider took it; a delivery row is staged.</summary>
    Sent,

    /// <summary>The provider refused it for good; a failed row is staged.</summary>
    Failed,

    /// <summary>The recipient reference resolved to nobody (S3); an unresolved row is staged and nothing retries it.</summary>
    Unresolved,

    /// <summary>The recipient turned the category off (S9); nothing is sent.</summary>
    OptedOut,

    /// <summary>The address is suppressed over the channel; nothing is sent.</summary>
    Suppressed,

    /// <summary>The delivery was already recorded (S14); nothing is sent.</summary>
    Duplicate,

    /// <summary>No channel can carry the kind to the recipient; nothing is sent.</summary>
    NoChannel,
}
