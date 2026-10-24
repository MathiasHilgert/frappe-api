namespace Frappe.Notification.Infrastructure.Resend;

/// <summary>Resend API settings. The key is a secret: set it through the environment, never in a committed file.</summary>
internal sealed class ResendOptions
{
    public const string Section = "Notification:Resend";

    public string? ApiKey { get; set; }

    public string From { get; set; } = "Frappé <no-reply@frappe.la>";

    public Uri BaseAddress { get; set; } = new("https://api.resend.com/");

    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);
}
