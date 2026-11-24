using Frappe.Identity.Application.Limits;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Credentials;
using Frappe.Results;

namespace Frappe.Identity.Application.Credentials;

/// <summary>
/// Asks a signed-in principal for their password again (a password change, a step-up). A session proves who opened it, not who is at the
/// keyboard, so a stolen session must not be able to guess the password: every recheck takes a slot first, keyed by the principal, whatever
/// its outcome, and only then is the password checked (<see cref="PasswordCheck"/>, with its floor and its one hash per refusal). When the
/// slots are gone the answer is a throttle; when they cannot be counted, none is judged (SEC-14).
/// </summary>
public sealed class PasswordRecheck(IAttemptLimiter limiter, PasswordCheck check)
{
    /// <summary>The platform's code for a throttled caller (<c>http.rate_limited</c>): its text is the platform's.</summary>
    public const string ThrottledCode = "http.rate_limited";

    /// <summary>
    /// Rechecks of one principal's password: 10 per 15 minutes, keyed by the principal's id, whatever their outcome, so a hijacked session can
    /// try ten guesses and then waits. Fails closed (SEC-14): it guards a password.
    /// </summary>
    public static readonly LimitFlow Flow = new("password.recheck", 10, TimeSpan.FromMinutes(15), LimitFailure.FailClosed);

    /// <summary>
    /// Whether <paramref name="candidate"/> is the password <paramref name="stored"/> was made from (<see langword="null"/> when the principal
    /// has none), or the error that refused the attempt before the password was looked at: a throttle (429) or a counter that is down (503). A
    /// wrong password is a plain <see langword="false"/>, never faster than <see cref="PasswordCheck.RefusalFloor"/>.
    /// </summary>
    public async Task<Result<bool>> MatchesAsync(Guid principalId, PasswordHash? stored, string? candidate, CancellationToken cancellationToken)
    {
        var verdict = await limiter.HitAsync(Flow, principalId.ToString("N"), cancellationToken);
        switch (verdict.Outcome)
        {
            case AttemptOutcome.Unavailable:
                return new UnavailableError(IdentityErrorCodes.AttemptsUnavailable);
            case AttemptOutcome.Refused:
                return new RateLimitedError(ThrottledCode, verdict.RetryAfter);
            default:
                return await check.MatchesAsync(stored, candidate, cancellationToken);
        }
    }
}
