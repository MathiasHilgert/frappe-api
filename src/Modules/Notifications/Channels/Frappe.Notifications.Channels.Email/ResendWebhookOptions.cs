namespace Frappe.Notifications.Channels.Email;

/// <summary>
/// How the webhook of Resend is authenticated, bound from <c>Notifications:Email:Resend</c> next to <see cref="ResendOptions"/> (a host that
/// only receives the webhook binds this one and needs no API key). The signing secret comes only from the environment
/// (<c>Notifications__Email__Resend__WebhookSecret</c>, CFG-10, S12).
/// </summary>
public sealed class ResendWebhookOptions
{
    /// <summary>The section the options are bound from.</summary>
    public const string Section = ResendOptions.Section;

    /// <summary>The prefix of the secrets Resend shows (Svix's format); a value without it is the same secret.</summary>
    public const string SecretPrefix = "whsec_";

    /// <summary>The shortest secret, in bytes once decoded: Svix issues longer ones, so a shorter value is a mistake.</summary>
    public const int MinimumSecretBytes = 16;

    /// <summary>
    /// <c>Notifications:Email:Resend:WebhookSecret</c>: the signing secret of the webhook endpoint (<c>whsec_</c> and base64, as Resend
    /// shows it). No default: required wherever the webhook is served. Range base64 of at least 16 bytes. Never logged.
    /// </summary>
    public string? WebhookSecret { get; set; }
}
