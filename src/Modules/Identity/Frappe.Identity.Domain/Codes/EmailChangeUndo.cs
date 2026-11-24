using Frappe.Aggregates;
using Frappe.Results;
using NodaTime;

// EF Core materializes the aggregate through its private constructor and setters.
#pragma warning disable S1144, S3453
namespace Frappe.Identity.Domain.Codes;

/// <summary>
/// The right to put an owner's earlier address back after an email change (S15): single-use, valid for <see cref="Lifetime"/>, bound to the
/// principal and to the pair of addresses of one change. Only the keyed hash of its token is stored. Tokens of different changes are live
/// together (a second change must not take away the way back from the first); only a token for the same pair replaces another, which the
/// database enforces.
/// </summary>
public sealed class EmailChangeUndo : AggregateRoot
{
    private EmailChangeUndo()
    {
        OldAddress = null!;
        NewAddress = null!;
        TokenHmac = null!;
    }

    private EmailChangeUndo(
        Guid id, Guid principalId, string oldAddress, string newAddress, CodeHmac tokenHmac, Instant now)
    {
        Id = id;
        PrincipalId = principalId;
        OldAddress = oldAddress;
        NewAddress = newAddress;
        TokenHmac = tokenHmac;
        IssuedAt = now;
        ExpiresAt = now + Lifetime;
    }

    /// <summary>How long the notice can be acted on after the change was confirmed (S15).</summary>
    public static readonly Duration Lifetime = Duration.FromDays(7);

    /// <summary>UUIDv7 made by the application (DATA-02); it travels in the token, so the route finds the row without the principal.</summary>
    public Guid Id { get; private set; }

    public Guid PrincipalId { get; private set; }

    /// <summary>The normalized address the change replaced: the one the token restores.</summary>
    public string OldAddress { get; private set; }

    /// <summary>The normalized address the change put in its place.</summary>
    public string NewAddress { get; private set; }

    /// <summary>The HMAC-SHA256 of the token's secret under the configured key.</summary>
    public CodeHmac TokenHmac { get; private set; }

    public Instant IssuedAt { get; private set; }

    public Instant ExpiresAt { get; private set; }

    public Instant? RedeemedAt { get; private set; }

    /// <summary>When a newer token for the same change, or another undo, made this one useless without redeeming it.</summary>
    public Instant? InvalidatedAt { get; private set; }

    /// <summary>Whether the token still holds its slot: neither redeemed nor invalidated, whatever the time (the unique index predicate).</summary>
    public bool HoldsLiveSlot => RedeemedAt is null && InvalidatedAt is null;

    /// <summary>Whether the token can be redeemed at <paramref name="now"/>: it holds its slot and has not expired.</summary>
    public bool IsLive(Instant now) => HoldsLiveSlot && now < ExpiresAt;

    /// <summary>
    /// A token of <paramref name="principalId"/> that restores <paramref name="oldAddress"/> after <paramref name="newAddress"/> replaced it.
    /// </summary>
    public static EmailChangeUndo Issue(Guid principalId, string oldAddress, string newAddress, CodeHmac tokenHmac, Instant now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(newAddress);
        ArgumentNullException.ThrowIfNull(tokenHmac);
        return new EmailChangeUndo(Guid.CreateVersion7(), principalId, oldAddress, newAddress, tokenHmac, now);
    }

    /// <summary>Makes the token useless without redeeming it. One that was redeemed or invalidated already stays as it was.</summary>
    public void Invalidate(Instant now)
    {
        if (HoldsLiveSlot)
        {
            InvalidatedAt = Instant.Max(now, IssuedAt);
        }
    }

    /// <summary>
    /// Spends the token at <paramref name="now"/>, once. One that expired, was redeemed or was invalidated answers
    /// <see cref="IdentityErrorCodes.UndoInvalid"/>, whichever it was.
    /// </summary>
    public Result<Unit> Redeem(Instant now)
    {
        if (!IsLive(now))
        {
            return Error.RuleViolation(IdentityErrorCodes.UndoInvalid);
        }

        RedeemedAt = Instant.Max(now, IssuedAt);
        return Unit.Value;
    }
}
