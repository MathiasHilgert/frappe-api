using Frappe.Identity.Domain.Sessions;
using NodaTime;

namespace Frappe.Identity.Application.Sessions;

/// <summary>
/// What queries read about sessions, projected without loading an aggregate (CQRS-08). It never changes anything (CQRS-07), and nothing it
/// returns is cached: a session is read from its source on every request (SEC-17).
/// </summary>
public interface ISessionReads
{
    /// <summary>
    /// The facts of the session whose token hashes to <paramref name="tokenHash"/> and of its principal, whatever their state; none when no
    /// session has that hash. Whether they let the holder in is <see cref="SessionAccess.IsActiveAt"/>'s to say.
    /// </summary>
    Task<SessionAccess?> FindAccessAsync(byte[] tokenHash, CancellationToken cancellationToken);

    /// <summary>The session <paramref name="id"/> as its holder sees it, or <see langword="null"/> when there is none.</summary>
    Task<CurrentSession?> FindCurrentAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Up to <paramref name="limit"/> live sessions (not revoked, not ended, neither expiry reached at <paramref name="now"/>) of
    /// <paramref name="principalId"/>, newest opened first and by id on a tie, starting right after <paramref name="after"/> (keyset, never an
    /// offset).
    /// </summary>
    Task<IReadOnlyList<ListedSession>> ListLiveAsync(
        Guid principalId, Instant now, int limit, SessionPosition? after, CancellationToken cancellationToken);
}
