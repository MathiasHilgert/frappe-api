namespace Frappe.Notifications.Senders.Email;

/// <summary>
/// Where the unsubscribe links of a Marketing mail point (S5, S9), bound from <c>Notifications:Unsubscribe</c> next to the signing key the module
/// reads from the same section. Only the host that renders mails (the Worker) binds it.
/// </summary>
public sealed class UnsubscribeLinkOptions
{
    /// <summary>The section the options are bound from.</summary>
    public const string Section = "Notifications:Unsubscribe";

    /// <summary>The route a mail client posts a one-click unsubscribe to, under <see cref="ApiBaseUrl"/>.</summary>
    internal const string OneClickPath = "v1/notifications/unsubscriptions";

    /// <summary>
    /// <c>Notifications:Unsubscribe:PageUrl</c>: the frontend page the visible link opens; the token is added as <c>?token=</c>. The page does not
    /// exist yet and the API serves no HTML (S5). No default: required where mails are rendered. Range an absolute http or https URL.
    /// </summary>
    public Uri? PageUrl { get; set; }

    /// <summary>
    /// <c>Notifications:Unsubscribe:ApiBaseUrl</c>: where this API is reachable by a mail client, ending in a slash (for example
    /// <c>https://api.example.com/</c>); the one-click route is under it. No default: required where mails are rendered. Range an absolute http
    /// or https URL ending in a slash.
    /// </summary>
    public Uri? ApiBaseUrl { get; set; }
}
