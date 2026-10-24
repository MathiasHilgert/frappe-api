using Frappe.Identity.Domain.Principals;

namespace Frappe.Identity.Application.Principals;

/// <summary>What authenticating a request needs to know about a session and its principal, and nothing more.</summary>
public sealed record SessionCredential(
    Guid PrincipalId,
    PrincipalType PrincipalType,
    OwnerStatus OwnerStatus,
    Guid SessionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt)
{
    /// <summary>Live by <see cref="Session.IsLive(DateTimeOffset, DateTimeOffset, TimeProvider)"/>, and not suspended.</summary>
    public bool Admits(TimeProvider clock) =>
        OwnerStatus != OwnerStatus.Suspended && Session.IsLive(CreatedAt, LastSeenAt, clock);
}
