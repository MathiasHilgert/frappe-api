using System.Text.Json.Serialization;

namespace Frappe.Notifications.Http.Unsubscriptions;

/// <summary>The form RFC 8058 gives a one-click unsubscribe: the field a mail client posts. The route documents it and reads nothing of it.</summary>
public sealed class UnsubscribeOneClick
{
    /// <summary>Always <c>One-Click</c>.</summary>
    [JsonPropertyName("List-Unsubscribe")]
    public string? ListUnsubscribe { get; init; }
}
