using System.Net;
using Frappe.Identity.Application.Limits;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Codes;
using Frappe.Results;
using NodaTime;

namespace Frappe.Identity.Application.Codes;

/// <summary>
/// Redeems a typed one-time code (S5). Every attempt takes guess slots first, in the limiter (a missing code costs what a wrong one does): two keyed by principal and purpose (10 a day
/// from the caller's network prefix, then 100 a day from every network, the second spent only when the first allowed the attempt) and then
/// one keyed by the code's id (5 per code). A failed <c>Result</c> rolls its unit of work back, so a counter in the database would never grow.
/// Only then is the text compared, in fixed time. When the slots are gone, or the code is wrong, expired, used or replaced, or there is no
/// code, the answer is the same <see cref="IdentityErrorCodes.CodeInvalid"/>: it tells nothing about which. When guesses cannot be counted
/// (the counter is down) none is judged and the answer is <see cref="IdentityErrorCodes.AttemptsUnavailable"/>.
/// </summary>
public sealed class CodeRedeemer(IOneTimeCodes codes, ICodeHasher hasher, IAttemptLimiter limiter, TimeProvider time)
{
    private const int Digits = 6;

    /// <summary>
    /// Spends the code of <paramref name="purpose"/> that <paramref name="principalId"/> holds, when <paramref name="candidate"/> is its text.
    /// The caller's command commits the redemption with whatever it activates.
    /// </summary>
    public async Task<Result<RedeemedCode>> RedeemAsync(Guid principalId, CodePurpose purpose, string? candidate, IPAddress? client,
        CancellationToken cancellationToken)
    {
        if (await TakePrincipalSlotsAsync(principalId, purpose, client, cancellationToken) is { } spent)
        {
            return Result.Failure<RedeemedCode>([spent]);
        }

        var code = await codes.LiveOfAsync(principalId, purpose, cancellationToken);
        if (code is null)
        {
            return Invalid();
        }

        if (await TakeSlotAsync(CodeLimits.Guess, CodeLimits.GuessSubject(code), cancellationToken) is { } exhausted)
        {
            return Result.Failure<RedeemedCode>([exhausted]);
        }

        return Judge(code, candidate);
    }

    /// <summary>
    /// Spends the code <paramref name="codeId"/> (as the caller typed it) of <paramref name="purpose"/>, when <paramref name="candidate"/> is its
    /// text, for a caller who does not know the principal: a password reset names its code, not its owner. The slot of the id is taken
    /// <b>before</b> the code is looked up, whatever the id is (unknown, malformed or one nobody was ever given), so a down counter refuses every
    /// request the same and a real id costs what an invented one does; then the principal's slot, once the code says whose it is.
    /// </summary>
    public async Task<Result<RedeemedCode>> RedeemByIdAsync(
        string? codeId, CodePurpose purpose, string? candidate, IPAddress? client, CancellationToken cancellationToken)
    {
        var parsed = Guid.TryParse(codeId, out var id);
        if (await TakeSlotAsync(CodeLimits.Guess, parsed ? CodeLimits.GuessSubject(id) : CodeLimits.MalformedSubject, cancellationToken) is { } slot)
        {
            return Result.Failure<RedeemedCode>([slot]);
        }

        var code = parsed ? await codes.FindAsync(id, cancellationToken) : null;
        if (code is null || code.Purpose != purpose || !code.HoldsLiveSlot)
        {
            return Invalid();
        }

        if (await TakePrincipalSlotsAsync(code.PrincipalId, purpose, client, cancellationToken) is { } spent)
        {
            return Result.Failure<RedeemedCode>([spent]);
        }

        return Judge(code, candidate);
    }

    private static Result<RedeemedCode> Invalid() => Error.RuleViolation(IdentityErrorCodes.CodeInvalid);

    private static bool HasCodeShape(string? candidate) =>
        candidate is { Length: Digits } && candidate.All(char.IsAsciiDigit);

    /// <summary>
    /// The principal's slots: the one of the caller's network prefix first, and the wide one only when it allowed the attempt, so one source can
    /// never spend what the owner's own network has left.
    /// </summary>
    private async Task<Error?> TakePrincipalSlotsAsync(Guid principalId, CodePurpose purpose, IPAddress? client,
        CancellationToken cancellationToken) =>
        await TakeSlotAsync(CodeLimits.GuessPerPrincipal, CodeLimits.PrincipalSubject(principalId, purpose, client), cancellationToken)
        ?? await TakeSlotAsync(CodeLimits.GuessPerPrincipalAnywhere, CodeLimits.PrincipalSubject(principalId, purpose), cancellationToken);

    /// <summary>The error that refuses the attempt (<see langword="null"/> when it may go on): the counter is down, or the slots are gone.</summary>
    private async Task<Error?> TakeSlotAsync(LimitFlow flow, string subject, CancellationToken cancellationToken)
    {
        var verdict = await limiter.HitAsync(flow, subject, cancellationToken);
        return verdict.Outcome switch
        {
            AttemptOutcome.Unavailable => new UnavailableError(IdentityErrorCodes.AttemptsUnavailable),
            AttemptOutcome.Refused => Error.RuleViolation(IdentityErrorCodes.CodeInvalid),
            _ => null,
        };
    }

    private Result<RedeemedCode> Judge(OneTimeCode code, string? candidate)
    {
        if (!HasCodeShape(candidate) || !hasher.Matches(code.CodeHmac, candidate!))
        {
            return Invalid();
        }

        return code.Redeem(Instant.FromDateTimeOffset(time.GetUtcNow()))
            .Map(_ => new RedeemedCode(code.Id, code.Purpose, code.Target, code.PrincipalId));
    }
}
