using System.ComponentModel.DataAnnotations;

namespace Frappe.Notification.Infrastructure.Smtp;

/// <summary>
/// The local mail catcher (Mailpit, see <c>deploy/compose.yaml</c>) that Development sends to when no Resend key is set.
/// Only read in Development: every other environment sends through Resend, so this can never deliver real mail.
/// </summary>
internal sealed class SmtpOptions
{
    public const string SectionName = "Notification:Smtp";

    /// <summary>Empty means no catcher: Development then logs mail instead.</summary>
    public string? Host { get; set; }

    [Range(1, 65_535)]
    public int Port { get; set; } = 1025;

    [Required]
    public string From { get; set; } = "Frappé <no-reply@frappe.local>";

    public bool HasHost => !string.IsNullOrWhiteSpace(Host);
}
