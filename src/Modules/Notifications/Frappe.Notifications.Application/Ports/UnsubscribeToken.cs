using Frappe.Results;

namespace Frappe.Notifications.Application.Ports;

/// <summary>The one refusal of an unsubscribe token that is not ours: it names nothing about why, so a forger learns nothing.</summary>
public static class UnsubscribeToken
{
    /// <summary>The error code of a token that is missing, malformed, tampered or unknown.</summary>
    public const string InvalidCode = "notifications.unsubscribe_token.invalid";

    /// <summary>The refusal a token reader answers.</summary>
    public static Error Invalid() => Error.InvalidValue(InvalidCode);
}
