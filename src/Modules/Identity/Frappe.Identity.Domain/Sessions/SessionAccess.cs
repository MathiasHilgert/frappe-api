using Frappe.Identity.Domain.Principals;
using NodaTime;

namespace Frappe.Identity.Domain.Sessions;

/// <summary>
/// What decides whether a request that presents a session's token is let in: the facts of the session and of its principal, read together.
/// It is the one place the rule lives (S2, S8), so a reader that projects the facts without loading the aggregate and the aggregate itself
/// (<see cref="Session.IsActiveFor"/>) agree. The principal's status and stamp are read on every request, never remembered.
/// </summary>
/// <param name="SessionId">The session.</param>
/// <param name="PrincipalId">The principal it belongs to.</param>
/// <param name="Kind">What the principal is.</param>
/// <param name="PrincipalStatus">Whether the principal may authenticate: only an active one does.</param>
/// <param name="PrincipalStamp">The principal's credential stamp now.</param>
/// <param name="SessionStamp">The credential stamp the session was opened (or restamped) under.</param>
/// <param name="Methods">How the holder proved who they are.</param>
/// <param name="LastSeenAt">When the session was last touched.</param>
/// <param name="IdleExpiresAt">The session idles out when unused by then.</param>
/// <param name="AbsoluteExpiresAt">The session ends by then, however much it is used.</param>
/// <param name="EndedAt">When the sweep ended it, if it did.</param>
/// <param name="RevokedAt">When it was revoked, if it was.</param>
/// <param name="StepUpUntil">Until when a recent password check counts, if one was made.</param>
public sealed record SessionAccess(
    Guid SessionId,
    Guid PrincipalId,
    PrincipalKind Kind,
    PrincipalStatus PrincipalStatus,
    Guid PrincipalStamp,
    Guid SessionStamp,
    IReadOnlyList<AuthenticationMethod> Methods,
    Instant LastSeenAt,
    Instant IdleExpiresAt,
    Instant AbsoluteExpiresAt,
    Instant? EndedAt,
    Instant? RevokedAt,
    Instant? StepUpUntil)
{
    /// <summary>
    /// Whether the session lets its principal in at <paramref name="now"/>: not ended, not revoked, neither idle nor absolute expiry reached
    /// (reaching the instant is expiring), the principal active, and the principal's stamp equal to the session's (equality, never order: a
    /// stamp is an identifier, not a counter).
    /// </summary>
    public bool IsActiveAt(Instant now) =>
        EndedAt is null
        && RevokedAt is null
        && now < IdleExpiresAt
        && now < AbsoluteExpiresAt
        && PrincipalStatus == PrincipalStatus.Active
        && PrincipalStamp == SessionStamp;
}
