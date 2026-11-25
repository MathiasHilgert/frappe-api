using Frappe.Localization;
using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.Application.Senders;

/// <summary>
/// What a channel did with one delivery. A transient fault (a 429, a 5xx, a timeout) is not an outcome: the channel throws, so the
/// message is redelivered (S14).
/// </summary>
public closed record ChannelOutcome
{
    private protected ChannelOutcome()
    {
    }

    /// <summary>The provider took the message.</summary>
    /// <param name="Provider">The provider that took it.</param>
    /// <param name="ProviderMessageId">The provider's id for it; feedback is matched by it.</param>
    /// <param name="LocaleServed">The language it was rendered in.</param>
    public sealed record Sent(DeliveryProvider Provider, string ProviderMessageId, LanguageTag? LocaleServed) : ChannelOutcome;

    /// <summary>The provider refused it for good (S14).</summary>
    /// <param name="Reason">Why, as a code.</param>
    public sealed record Failed(DeliveryFailureReason Reason) : ChannelOutcome;
}
