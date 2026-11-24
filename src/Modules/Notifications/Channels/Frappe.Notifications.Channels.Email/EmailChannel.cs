using Frappe.Notifications.Application.Channels;
using Frappe.Notifications.Application.Kinds;
using Frappe.Notifications.Channels.Email.Rendering;
using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.Channels.Email;

/// <summary>
/// The email channel (S0): it composes the kind's draft, renders it to HTML and text from the same blocks and hands it to
/// <see cref="IEmailSender"/>. A category that can be turned off (Marketing) gets its visible unsubscribe link and the
/// <c>List-Unsubscribe</c> headers added here, so no kind author writes them (S9).
/// </summary>
public sealed class EmailChannel(
    EmailComposer composer,
    RazorEmailRenderer html,
    IcuNotificationTexts texts,
    IEmailUnsubscribeLinks unsubscribeLinks,
    IEmailSender sender) : INotificationChannel
{
    /// <summary>The kind whose texts belong to the channel itself (the unsubscribe sentence).</summary>
    internal const string ChannelTexts = "email_channel";

    private static readonly IReadOnlyDictionary<string, string> NoHeaders = new Dictionary<string, string>();

    /// <summary>The channel's name, which a kind's recipient names too (<c>Recipient.Contact(EmailChannel.Channel, ...)</c>).</summary>
    public static ChannelName Channel { get; } = ChannelName.Create("email").Value;

    public ChannelName Name => Channel;

    public bool CanDeliver(NotificationKind kind) => composer.CanCompose(kind);

    public async Task<ChannelOutcome> DeliverAsync(DispatchItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        var address = EmailAddress.Create(item.Contact.Address);
        if (!address.IsSuccess)
        {
            return new ChannelOutcome.Failed(DeliveryFailureReason.InvalidRecipient);
        }

        ComposedEmail composed;
        try
        {
            composed = composer.Compose(item.Kind, item.Payload, item.Contact.Language);
        }
        catch (ArgumentException)
        {
            // The kind cannot build a draft for this payload (a link that is not https, a Security draft with a link): trying again
            // gives the same, so it fails for good instead of going back to the queue until it is dead-lettered.
            return new ChannelOutcome.Failed(DeliveryFailureReason.ProviderRejected);
        }

        var (footer, headers) = item.Kind.Category.CanOptOut ? Unsubscribe(item, composed) : (null, NoHeaders);
        var document = new EmailDocument(
            composed.Language, composed.Draft.Subject, composed.Draft.PreheaderText, composed.Draft.Blocks, footer);
        var rendered = new RenderedEmail(
            address.Value,
            composed.Draft.Subject,
            await html.RenderAsync(document),
            PlainTextEmailRenderer.Render(document),
            headers,
            item.IdempotencyKey,
            composed.Language);

        var outcome = await sender.SendAsync(rendered, cancellationToken);
        return outcome is ChannelOutcome.Sent sent ? sent with { LocaleServed = composed.Language } : outcome;
    }

    private (UnsubscribeFooter, IReadOnlyDictionary<string, string>) Unsubscribe(DispatchItem item, ComposedEmail composed)
    {
        var link = unsubscribeLinks.LinkFor(item.RecipientKey, item.Kind.Category);
        var channelTexts = texts.For(ChannelTexts, composed.Language);
        var footer = new UnsubscribeFooter(channelTexts["unsubscribe_text"], channelTexts["unsubscribe_label"], link.PageUrl);
        var headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["List-Unsubscribe"] = $"<{link.OneClickUrl.AbsoluteUri}>",
            ["List-Unsubscribe-Post"] = "List-Unsubscribe=One-Click",
        };
        return (footer, headers);
    }
}
