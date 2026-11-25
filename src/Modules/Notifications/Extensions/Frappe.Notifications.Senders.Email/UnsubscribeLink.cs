namespace Frappe.Notifications.Senders.Email;

/// <summary>The two addresses an unsubscribe has (S5): the page the visible link opens and the endpoint a mail client posts to (RFC 8058).</summary>
/// <param name="PageUrl">The configurable frontend page; the visible link.</param>
/// <param name="OneClickUrl">The signed one-click endpoint; the <c>List-Unsubscribe</c> header.</param>
public sealed record UnsubscribeLink(Uri PageUrl, Uri OneClickUrl)
{
    public override string ToString() => "UnsubscribeLink { PageUrl = ***, OneClickUrl = *** }";
}
