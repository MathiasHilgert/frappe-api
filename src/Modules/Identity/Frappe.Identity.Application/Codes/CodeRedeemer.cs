using Frappe.Identity.Application.Limits;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Codes;
using Frappe.Results;
using NodaTime;

namespace Frappe.Identity.Application.Codes;

/// <summary>
/// Redeems a typed one-time code (S5). Every attempt at a code takes a guess slot first, in the limiter keyed by the code's id (5 per code):
/// a failed <c>Result</c> rolls its unit of work back, so a counter in the database would never grow. Only then is the text compared, in fixed
/// time. When the slots are gone, or the code is wrong, expired, used or replaced, or there is no code, the answer is the same
/// <see cref="IdentityErrorCodes.CodeInvalid"/>: it tells nothing about which. When guesses cannot be counted (the counter is down) none is
/// judged and the answer is <see cref="IdentityErrorCodes.AttemptsUnavailable"/>.
/// </summary>
public sealed class CodeRedeemer(IOneTimeCodes codes, ICodeHasher hasher, IAttemptLimiter limiter, TimeProvider time)
{
    private const int Digits = 6;

    /// <summary>
    /// Spends the code of <paramref name="purpose"/> that <paramref name="principalId"/> holds, when <paramref name="candidate"/> is its text.
    /// The caller's command commits the redemption with whatever it activates.
    /// </summary>
    public async Task<Result<RedeemedCode>> RedeemAsync(Guid principalId, CodePurpose purpose, string? candidate, CancellationToken cancellationToken)
    {
        var code = await codes.LiveOfAsync(principalId, purpose, cancellationToken);
        if (code is null)
        {
            return Error.RuleViolation(IdentityErrorCodes.CodeInvalid);
        }

        var verdict = await limiter.HitAsync(CodeLimits.Guess, CodeLimits.GuessSubject(code), cancellationToken);
        if (verdict.Outcome == AttemptOutcome.Unavailable)
        {
            return new UnavailableError(IdentityErrorCodes.AttemptsUnavailable);
        }

        if (verdict.Outcome == AttemptOutcome.Refused || !HasCodeShape(candidate) || !hasher.Matches(code.CodeHmac, candidate!))
        {
            return Error.RuleViolation(IdentityErrorCodes.CodeInvalid);
        }

        return code.Redeem(Instant.FromDateTimeOffset(time.GetUtcNow())).Map(_ => new RedeemedCode(code.Id, code.Purpose, code.Target));
    }

    private static bool HasCodeShape(string? candidate) =>
        candidate is { Length: Digits } && candidate.All(char.IsAsciiDigit);
}
