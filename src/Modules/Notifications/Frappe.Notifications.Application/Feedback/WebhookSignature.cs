using Frappe.Results;

namespace Frappe.Notifications.Application.Feedback;

/// <summary>The one refusal of a webhook call that is not authentic: it names nothing about why, so a forger learns nothing.</summary>
public static class WebhookSignature
{
    /// <summary>The error code of a call whose signature is missing, wrong or too old.</summary>
    public const string InvalidCode = "notifications.webhook_signature.invalid";

    /// <summary>How far a call's signed timestamp may be from the clock, either way: an older call is refused as a replay.</summary>
    public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    /// <summary>The refusal a parser answers for a call that is not authentic.</summary>
    public static Error Invalid() => Error.InvalidValue(InvalidCode);
}
