namespace Frappe.Notification.Infrastructure.Resend;

/// <summary>The body of Resend's <c>POST /emails</c>, serialized through <see cref="ResendJsonContext"/>.</summary>
internal sealed record ResendEmail(
    string From,
    IReadOnlyList<string> To,
    string Subject,
    string Text,
    string? Html,
    IReadOnlyList<ResendAttachment>? Attachments);
