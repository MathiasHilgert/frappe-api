using Frappe.Aggregates;
using NodaTime;

// EF Core materializes the aggregate through its private constructor and setters.
#pragma warning disable S1144, S3453
namespace Frappe.Identity.Domain.Credentials;

/// <summary>The password of a principal, kept as the opaque encoded hash its hasher produced (never the password).</summary>
public sealed class PasswordCredential : AggregateRoot
{
    private PasswordCredential() => Hash = null!;

    private PasswordCredential(Guid principalId, string hash, Instant now)
    {
        PrincipalId = principalId;
        Hash = hash;
        ChangedAt = now;
    }

    /// <summary>The principal it belongs to; also its key, as a principal has at most one password.</summary>
    public Guid PrincipalId { get; private set; }

    /// <summary>The encoded Argon2id hash, with its own parameters and salt.</summary>
    public string Hash { get; private set; }

    public Instant ChangedAt { get; private set; }

    /// <summary>The password of <paramref name="principalId"/>, set at <paramref name="now"/>.</summary>
    public static PasswordCredential Set(Guid principalId, PasswordHash hash, Instant now)
    {
        ArgumentNullException.ThrowIfNull(hash);
        return new PasswordCredential(principalId, hash.Value, now);
    }

    /// <summary>Puts <paramref name="hash"/> in place of the current one, at <paramref name="now"/>.</summary>
    public void Replace(PasswordHash hash, Instant now)
    {
        ArgumentNullException.ThrowIfNull(hash);
        Hash = hash.Value;
        ChangedAt = now;
    }
}
