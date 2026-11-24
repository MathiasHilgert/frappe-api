using Frappe.Identity.Domain.Sessions;

namespace Frappe.Identity.Application.Sessions;

/// <summary>The sessions of principals. It stages and never saves: the command's unit of work does (UOW-01).</summary>
public interface ISessions
{
    /// <summary>The session <paramref name="id"/>, tracked for change, or <see langword="null"/> when there is none.</summary>
    Task<Session?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Every session of <paramref name="principalId"/> that is neither revoked nor ended, tracked for change, so a credential change can revoke
    /// or restamp each through its aggregate (and so raise its events) in the unit that changes the credential. No bulk SQL (UOW-03).
    /// </summary>
    Task<IReadOnlyList<Session>> ListLiveOfAsync(Guid principalId, CancellationToken cancellationToken);

    /// <summary>Stages <paramref name="session"/> to be inserted with the unit of work.</summary>
    void Add(Session session);
}
