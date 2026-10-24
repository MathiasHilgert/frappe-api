namespace Frappe.Notification.Infrastructure.Resend;

/// <summary>An attachment; with a content id it is shown inline where the HTML references <c>cid:&lt;id&gt;</c>.</summary>
internal sealed record ResendAttachment(string Filename, string Content, string ContentType, string? ContentId);
