using Frappe.Notifications.Application.Feedback;
using Frappe.Results;

namespace Frappe.Notifications.Application.Ports;

/// <summary>
/// Authenticates and reads one provider's webhook (the channel plug-in implements it, registered as a keyed service named
/// <see cref="ProviderFeedbackParsers"/>). The core never sees the provider's payload shape.
/// </summary>
public interface IProviderFeedbackParser
{
    /// <summary>
    /// Verifies the call's signature and reads the event. A call that is not authentic fails with
    /// <c>notifications.webhook_signature.invalid</c>; an authentic event the core does not track (or cannot read) succeeds with
    /// <see langword="null"/>, so the provider is answered like any other and never retries it.
    /// </summary>
    Result<ProviderFeedback?> Parse(ProviderWebhook webhook);
}
