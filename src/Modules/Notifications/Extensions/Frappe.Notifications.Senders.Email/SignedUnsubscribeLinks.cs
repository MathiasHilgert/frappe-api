using Frappe.Notifications.Application.Ports;
using Frappe.Notifications.Domain.Deliveries;
using Microsoft.Extensions.Options;

namespace Frappe.Notifications.Senders.Email;

/// <summary>
/// Makes the unsubscribe links of a Marketing mail from the signed token (S5, S9): the one-click URL a mail client posts to
/// (<c>List-Unsubscribe</c>) and the page of the frontend the visible link opens. Both carry the same token, which is deterministic, so a
/// mail renders alike on every attempt (S14).
/// </summary>
internal sealed class SignedUnsubscribeLinks(IUnsubscribeTokens tokens, IOptions<UnsubscribeLinkOptions> options) : IEmailUnsubscribeLinks
{
    public UnsubscribeLink LinkFor(RecipientKey recipient, NotificationCategory category)
    {
        var token = tokens.Issue(recipient, category);
        var value = options.Value;
        var page = value.PageUrl!;
        var separator = string.IsNullOrEmpty(page.Query) ? '?' : '&';
        return new UnsubscribeLink(
            new Uri($"{page.AbsoluteUri}{separator}token={token}"),
            new Uri(value.ApiBaseUrl!, $"{UnsubscribeLinkOptions.OneClickPath}?token={token}"));
    }
}
