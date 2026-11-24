using Frappe.Aggregates;
using Frappe.Results;
using NodaTime;

// EF Core materializes the aggregate through its private constructor and setters.
#pragma warning disable S1144, S3453
namespace Frappe.Identity.Domain.Codes;

/// <summary>
/// A short code that proves control of an address for one purpose. Only its keyed hash is stored; at most one code per principal and purpose is
/// live (neither redeemed nor invalidated), which the database enforces.
/// </summary>
public sealed class OneTimeCode : AggregateRoot
{
    private OneTimeCode()
    {
        Target = null!;
        CodeHmac = null!;
    }

    private OneTimeCode(Guid id, Guid principalId, CodePurpose purpose, string target, CodeHmac codeHmac, Instant now, Duration lifetime)
    {
        Id = id;
        PrincipalId = principalId;
        Purpose = purpose;
        Target = target;
        CodeHmac = codeHmac;
        IssuedAt = now;
        ExpiresAt = now + lifetime;
    }

    /// <summary>How long a code can be redeemed after it was issued (S5).</summary>
    public static readonly Duration Lifetime = Duration.FromMinutes(15);

    /// <summary>UUIDv7 made by the application (DATA-02).</summary>
    public Guid Id { get; private set; }

    public Guid PrincipalId { get; private set; }

    public CodePurpose Purpose { get; private set; }

    /// <summary>The normalized address the code proves: the one it was sent to (the new address for an email change).</summary>
    public string Target { get; private set; }

    /// <summary>The HMAC-SHA256 of the code under the configured key.</summary>
    public CodeHmac CodeHmac { get; private set; }

    public Instant IssuedAt { get; private set; }

    public Instant ExpiresAt { get; private set; }

    public Instant? RedeemedAt { get; private set; }

    /// <summary>When a newer code or a change of state made this one useless without redeeming it.</summary>
    public Instant? InvalidatedAt { get; private set; }

    /// <summary>
    /// A code of <paramref name="purpose"/> for <paramref name="principalId"/>, valid for <paramref name="lifetime"/> from <paramref name="now"/>.
    /// </summary>
    public static OneTimeCode Issue(
        Guid principalId, CodePurpose purpose, string target, CodeHmac codeHmac, Instant now, Duration lifetime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentNullException.ThrowIfNull(codeHmac);
        return new OneTimeCode(Guid.CreateVersion7(), principalId, purpose, target, codeHmac, now, lifetime);
    }

    /// <summary>
    /// Whether the code still holds the principal's live slot: neither redeemed nor invalidated, whatever the time. It is the predicate of the
    /// unique index, so an expired code nobody redeemed still holds the slot until a newer one invalidates it.
    /// </summary>
    public bool HoldsLiveSlot => RedeemedAt is null && InvalidatedAt is null;

    /// <summary>Whether the code can be redeemed at <paramref name="now"/>: it holds the live slot and has not expired.</summary>
    public bool IsLive(Instant now) => HoldsLiveSlot && now < ExpiresAt;

    /// <summary>
    /// Makes the code useless without redeeming it, because a newer one replaces it (or a change of state made it moot). A code that was
    /// redeemed or invalidated already stays as it was.
    /// </summary>
    public void Invalidate(Instant now)
    {
        if (HoldsLiveSlot)
        {
            InvalidatedAt = Instant.Max(now, IssuedAt);
        }
    }

    /// <summary>
    /// Spends the code at <paramref name="now"/>, once. A code that expired, was redeemed or was invalidated answers
    /// <see cref="IdentityErrorCodes.CodeInvalid"/>, whichever it was: the caller never learns which.
    /// </summary>
    public Result<Unit> Redeem(Instant now)
    {
        if (!IsLive(now))
        {
            return Error.RuleViolation(IdentityErrorCodes.CodeInvalid);
        }

        RedeemedAt = Instant.Max(now, IssuedAt);
        return Unit.Value;
    }
}
