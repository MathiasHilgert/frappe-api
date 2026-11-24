using Frappe.Aggregates;
using NodaTime;

// EF Core materializes the aggregate through its private constructor and setters.
#pragma warning disable S1144, S3453
namespace Frappe.Identity.Domain.Principals;

/// <summary>
/// Who may authenticate, whatever kind of actor it is. It knows nothing of owners, staff or terminals: each kind keeps its own data in its own
/// feature. Its credential stamp changes with every credential change, so a session opened under an older stamp is rejected at once (S2).
/// </summary>
public sealed class Principal : AggregateRoot
{
    private Principal()
    {
    }

    private Principal(Guid id, PrincipalKind kind, PrincipalStatus status, Guid credentialStamp, Instant now)
    {
        Id = id;
        Kind = kind;
        Status = status;
        CredentialStamp = credentialStamp;
        CreatedAt = now;
    }

    /// <summary>UUIDv7 made by the application (DATA-02).</summary>
    public Guid Id { get; private set; }

    public PrincipalKind Kind { get; private set; }

    public PrincipalStatus Status { get; private set; }

    /// <summary>A UUIDv7 compared by equality and replaced whenever a credential changes; never ordered.</summary>
    public Guid CredentialStamp { get; private set; }

    public Instant CreatedAt { get; private set; }

    /// <summary>A principal of <paramref name="kind"/> in <paramref name="status"/>, with a fresh credential stamp.</summary>
    public static Principal Create(PrincipalKind kind, PrincipalStatus status, Instant now) =>
        new(Guid.CreateVersion7(), kind, status, Guid.CreateVersion7(), now);
}
