namespace Frappe.Notifications.Application.Messages;

/// <summary>A payload that has rules beyond its shape, such as a bound on a text; <see cref="NotificationPayloads"/> asks it after reading.</summary>
public interface IPayloadRules
{
    /// <summary>The JSON names of the fields that break a rule; none when the payload is fine. It never carries a value (S13).</summary>
    IReadOnlyList<string> InvalidFields();
}
