namespace Frappe.Identity.Application.Sessions;

/// <summary>
/// The authentication hot path's one write: moves a session's idle expiry with a single conditional row update,
/// without loading or versioning the principal, so a touch never races the owner's own changes.
/// </summary>
public interface ISessionTouches
{
    /// <summary>
    /// Sets the session's last use to <paramref name="now"/> when it was last used before
    /// <paramref name="lastSeenBefore"/>. False when nothing changed: another request touched it first, or it is gone.
    /// </summary>
    Task<bool> TouchAsync(Guid sessionId, DateTimeOffset now, DateTimeOffset lastSeenBefore, CancellationToken cancellationToken);
}
