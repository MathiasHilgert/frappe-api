namespace Frappe.Notifications.Application.Messages;

/// <summary>
/// A channel's trial of a payload: what only the channel knows about a payload that its kind cannot say (an email's action goes to an
/// absolute https url), asked where the request is accepted so the route refuses it as <c>notifications.payload.invalid</c> and not the
/// send, long after (S1).
/// </summary>
public interface INotificationPayloadCheck
{
    /// <summary>Whether the channel can make a notification of <paramref name="kind"/> out of <paramref name="payload"/>.</summary>
    bool Accepts(NotificationKind kind, object payload);
}
