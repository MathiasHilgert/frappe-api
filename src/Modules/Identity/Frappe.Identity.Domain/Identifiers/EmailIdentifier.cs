using Frappe.Aggregates;
using NodaTime;

// EF Core materializes the aggregate through its private constructor and setters.
#pragma warning disable S1144, S3453
namespace Frappe.Identity.Domain.Identifiers;

/// <summary>The email address a principal signs in with. The address is unique across principals by its normalized form.</summary>
public sealed class EmailIdentifier : AggregateRoot
{
    private EmailIdentifier()
    {
        Address = null!;
        NormalizedAddress = null!;
    }

    private EmailIdentifier(Guid id, Guid principalId, string address, string normalizedAddress, Instant? verifiedAt)
    {
        Id = id;
        PrincipalId = principalId;
        Address = address;
        NormalizedAddress = normalizedAddress;
        VerifiedAt = verifiedAt;
    }

    /// <summary>UUIDv7 made by the application (DATA-02).</summary>
    public Guid Id { get; private set; }

    public Guid PrincipalId { get; private set; }

    /// <summary>The address as the person typed it; mail is sent to it.</summary>
    public string Address { get; private set; }

    /// <summary>The address trimmed and lower-cased: what uniqueness and lookup compare.</summary>
    public string NormalizedAddress { get; private set; }

    /// <summary>When this address was proven to be the principal's; empty until then, and replaced when the address changes.</summary>
    public Instant? VerifiedAt { get; private set; }

    /// <summary>
    /// An address of <paramref name="principalId"/>; <paramref name="normalizedAddress"/> is what the caller's email value object normalized.
    /// </summary>
    public static EmailIdentifier Create(Guid principalId, string address, string normalizedAddress, Instant? verifiedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedAddress);
        return new EmailIdentifier(Guid.CreateVersion7(), principalId, address, normalizedAddress, verifiedAt);
    }
}
