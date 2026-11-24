using System.Security.Cryptography;
using Frappe.Identity.Application.Limits;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Codes;
using Frappe.Results;
using NodaTime;

namespace Frappe.Identity.Application.Codes;

/// <summary>
/// Issues a one-time code (S5): six random digits, stored only as their HMAC, valid for <see cref="OneTimeCode.Lifetime"/>. It invalidates the
/// code the principal holds for that purpose and stages the new one in the same unit of work, so at most one is ever live (the unique index
/// decides if two issues race). The budget of sends is counted first, in the limiter and outside the unit: a refusal changes nothing, so the
/// code the principal already holds keeps working. The plaintext is returned once and never stored.
/// </summary>
public sealed class CodeIssuer(IOneTimeCodes codes, ICodeHasher hasher, IAttemptLimiter limiter, TimeProvider time)
{
    private const int Digits = 6;

    private const int Combinations = 1_000_000;

    /// <summary>
    /// A new code of <paramref name="purpose"/> for <paramref name="principalId"/> proving <paramref name="target"/> (the normalized address
    /// it is sent to). Fails with <see cref="IdentityErrorCodes.CodeSendLimited"/> (a rate limit) when the principal has had its sends of this
    /// purpose for the hour; a caller that must answer the same either way ignores the failure.
    /// </summary>
    public async Task<Result<IssuedCode>> IssueAsync(Guid principalId, CodePurpose purpose, string target, CancellationToken cancellationToken)
    {
        var verdict = await limiter.HitAsync(CodeLimits.Send, CodeLimits.SendSubject(principalId, purpose), cancellationToken);
        if (verdict.Outcome == AttemptOutcome.Refused)
        {
            return new RateLimitedError(IdentityErrorCodes.CodeSendLimited, verdict.RetryAfter);
        }

        var now = Instant.FromDateTimeOffset(time.GetUtcNow());
        var live = await codes.LiveOfAsync(principalId, purpose, cancellationToken);
        live?.Invalidate(now);

        var plaintext = RandomNumberGenerator.GetInt32(Combinations).ToString($"D{Digits}", System.Globalization.CultureInfo.InvariantCulture);
        var code = OneTimeCode.Issue(principalId, purpose, target, hasher.Hash(plaintext), now, OneTimeCode.Lifetime);
        codes.Add(code);
        return new IssuedCode(code.Id, plaintext, code.ExpiresAt);
    }
}
