using Frappe.Identity.Application.Limits;
using Frappe.Identity.Domain.Codes;

namespace Frappe.Identity.Application.Codes;

/// <summary>The limits on one-time codes (S5): the flows <see cref="CodeIssuer"/> and <see cref="CodeRedeemer"/> count under.</summary>
public static class CodeLimits
{
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

    /// <summary>The subject the guesses at <paramref name="code"/> are counted under.</summary>
    internal static string GuessSubject(OneTimeCode code) => code.Id.ToString("N");

    /// <summary>The subject the sends of <paramref name="purpose"/> to <paramref name="principalId"/> are counted under.</summary>
    internal static string SendSubject(Guid principalId, CodePurpose purpose) => $"{purpose}:{principalId:N}";
}
