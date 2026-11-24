namespace Frappe.Identity.Domain;

/// <summary>The error codes of the Identity module (DOM-03). A published code is never renamed or reused.</summary>
public static class IdentityErrorCodes
{
    /// <summary>A password has fewer than <see cref="Credentials.Password.MinLength"/> characters (parameter <c>min</c>).</summary>
    public const string PasswordTooShort = "identity.password.too_short";

    /// <summary>A password has more than <see cref="Credentials.Password.MaxLength"/> characters (parameter <c>max</c>).</summary>
    public const string PasswordTooLong = "identity.password.too_long";

    /// <summary>A password appears in a public breach list.</summary>
    public const string PasswordBreached = "identity.password.breached";

    /// <summary>
    /// A one-time code did not redeem: wrong, expired, used, superseded or unknown. One answer for all of them, so the caller learns nothing.
    /// </summary>
    public const string CodeInvalid = "identity.code.invalid";

    /// <summary>A code was asked for more often than the budget of its purpose allows (parameter <c>retryAfter</c>, in seconds).</summary>
    public const string CodeSendLimited = "identity.code.send_limited";

    /// <summary>The counter of wrong guesses could not be reached, so no guess is judged: a limit that guards a secret fails closed.</summary>
    public const string AttemptsUnavailable = "identity.attempts.unavailable";

    /// <summary>
    /// The request carries no session, or one that is unknown, expired, revoked or opened under credentials that changed; also a duplicated
    /// session cookie. One answer for all of them (401 with <c>WWW-Authenticate</c>), so the caller learns nothing about which it was.
    /// </summary>
    public const string SessionInvalid = "identity.session.invalid";

    /// <summary>An email address is missing or is not shaped like one.</summary>
    public const string EmailInvalid = "identity.email.invalid";

    /// <summary>A name is missing its given part, is longer than a mail can greet with, or looks like a link.</summary>
    public const string NameInvalid = "identity.name.invalid";

    /// <summary>A code hash is not 32 bytes (parameter <c>length</c>); a bug of the caller, never shown to a client.</summary>
    internal const string CodeHmacLength = "identity.code.hmac_length";

    /// <summary>An owner that is not pending was asked to take a registration over; a bug of the caller, never shown to a client.</summary>
    internal const string OwnerNotPending = "identity.owner.not_pending";
}
