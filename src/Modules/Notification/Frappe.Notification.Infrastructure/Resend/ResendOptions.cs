using System.ComponentModel.DataAnnotations;

namespace Frappe.Notification.Infrastructure.Resend;

/// <summary>Resend API settings. The key is a secret: set it through the environment, never in a committed file.</summary>
internal sealed class ResendOptions
{
    public const string SectionName = "Notification:Resend";

    public string? ApiKey { get; set; }

    [Required]
    public string From { get; set; } = "Frappé <no-reply@frappe.la>";

    [Required]
    public Uri BaseAddress { get; set; } = new("https://api.resend.com/");

    /// <summary>Per attempt. Short, because the durable queue owns retries; a hung call must not hold a worker.</summary>
    [Range(1, 120)]
    public int TimeoutSeconds { get; set; } = 10;

    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);
}
