using Frappe.Aggregates;
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

    private OneTimeCode(Guid id, Guid principalId, CodePurpose purpose, string target, byte[] codeHmac, Instant now, Duration lifetime)
    {
        Id = id;
        PrincipalId = principalId;
        Purpose = purpose;
        Target = target;
        CodeHmac = codeHmac;
        IssuedAt = now;
        ExpiresAt = now + lifetime;
    }

    /// <summary>UUIDv7 made by the application (DATA-02).</summary>
    public Guid Id { get; private set; }

    public Guid PrincipalId { get; private set; }

    public CodePurpose Purpose { get; private set; }

    /// <summary>The normalized address the code proves: the one it was sent to (the new address for an email change).</summary>
    public string Target { get; private set; }

    /// <summary>The HMAC-SHA256 of the code under the configured key.</summary>
    public byte[] CodeHmac { get; private set; }

    public Instant IssuedAt { get; private set; }

    public Instant ExpiresAt { get; private set; }

    public Instant? RedeemedAt { get; private set; }

    /// <summary>When a newer code or a change of state made this one useless without redeeming it.</summary>
    public Instant? InvalidatedAt { get; private set; }

    /// <summary>
    /// A code of <paramref name="purpose"/> for <paramref name="principalId"/>, valid for <paramref name="lifetime"/> from <paramref name="now"/>.
    /// </summary>
    public static OneTimeCode Issue(
        Guid principalId, CodePurpose purpose, string target, byte[] codeHmac, Instant now, Duration lifetime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentNullException.ThrowIfNull(codeHmac);
        return new OneTimeCode(Guid.CreateVersion7(), principalId, purpose, target, codeHmac, now, lifetime);
    }
}
