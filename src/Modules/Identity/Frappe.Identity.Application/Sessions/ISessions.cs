using Frappe.Identity.Domain.Sessions;
using NodaTime;

namespace Frappe.Identity.Application.Sessions;

/// <summary>The sessions of principals. It stages and never saves: the command's unit of work does (UOW-01).</summary>
public interface ISessions
{
    /// <summary>The session <paramref name="id"/>, tracked for change, or <see langword="null"/> when there is none.</summary>
    Task<Session?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// The sessions of <paramref name="principalId"/> that are live at <paramref name="now"/> (not revoked, not ended, neither expiry reached),
    /// tracked for change, so revoking or restamping one goes through its aggregate and raises its events (S13). No bulk SQL (UOW-03).
    /// </summary>
    Task<IReadOnlyList<Session>> ListLiveOfAsync(Guid principalId, Instant now, CancellationToken cancellationToken);

    /// <summary>Stages <paramref name="session"/> to be inserted with the unit of work.</summary>
    void Add(Session session);
}
