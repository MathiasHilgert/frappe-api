using Frappe.Identity.Application.Limits;
using Frappe.Identity.Domain.Codes;

namespace Frappe.Identity.Application.Codes;

/// <summary>The limits on one-time codes (S5): the flows <see cref="CodeIssuer"/> and <see cref="CodeRedeemer"/> count under.</summary>
public static class CodeLimits
{
    /// <summary>The one subject every id that is not an id is counted under, so junk never makes a counter of its own.</summary>
    internal const string MalformedSubject = "malformed";

    /// <summary>
    /// Guesses at one code, keyed by the code's id: 5 per code lifetime, so a code dies after five attempts whatever they were. Fails closed:
    /// while guesses cannot be counted none is judged (SEC-14).
    /// </summary>
    public static readonly LimitFlow Guess = new("code.guess", 5, OneTimeCode.Lifetime.ToTimeSpan(), LimitFailure.FailClosed);

    /// <summary>
    /// Codes sent for one purpose to one principal: 5 per hour, keyed by both. Fails open (SEC-14): it protects an inbox, not a secret, so a
    /// counter outage must not stop people from registering or resetting.
    /// </summary>
    public static readonly LimitFlow Send = new("code.send", 5, TimeSpan.FromHours(1), LimitFailure.FailOpen);

    /// <summary>
    /// Guesses at the codes of one purpose of one principal, across every code they were ever issued: 10 per 24 hours, keyed by principal and
    /// purpose, so asking for a new code (a new id, a new 5 guesses) never buys more than 10 guesses a day. Fails closed (SEC-14). It is taken
    /// before the slot of the code itself.
    /// </summary>
    public static readonly LimitFlow GuessPerPrincipal = new("code.guess.principal", 10, TimeSpan.FromHours(24), LimitFailure.FailClosed);

    /// <summary>The subject the guesses at the codes of <paramref name="purpose"/> of <paramref name="principalId"/> are counted under.</summary>
    internal static string PrincipalSubject(Guid principalId, CodePurpose purpose) => $"{purpose}:{principalId:N}";

    /// <summary>The subject the guesses at <paramref name="code"/> are counted under.</summary>
    internal static string GuessSubject(OneTimeCode code) => GuessSubject(code.Id);

    /// <summary>The subject the guesses at the code <paramref name="id"/> are counted under.</summary>
    internal static string GuessSubject(Guid id) => id.ToString("N");

    /// <summary>The subject the sends of <paramref name="purpose"/> to <paramref name="principalId"/> are counted under.</summary>
    internal static string SendSubject(Guid principalId, CodePurpose purpose) => $"{purpose}:{principalId:N}";
}
