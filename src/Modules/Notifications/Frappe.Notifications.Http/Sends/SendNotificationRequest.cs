using System.Text.Json;

namespace Frappe.Notifications.Http.Sends;

/// <summary>The body of <c>POST /v1/notifications/sends</c>: what to send and the data it needs.</summary>
/// <param name="Kind">The kind's name (<c>account_activation_code</c>), as the catalog knows it.</param>
/// <param name="Payload">The kind's payload, a JSON object whose fields the kind defines.</param>
public sealed record SendNotificationRequest(string? Kind, JsonElement Payload);
