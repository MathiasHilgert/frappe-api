using Frappe.Identity.Application.Sessions;

namespace Frappe.Identity.Application.Principals;

/// <summary>
/// Reads owners for queries: no-tracking projections straight to the shapes callers see, never an aggregate. Live
/// and fresh are judged against <c>now</c> with the same rules the aggregate applies.
/// </summary>
public interface IOwnerReads
{
    /// <summary>The owner's own profile, or null when the principal is gone.</summary>
    Task<OwnerProfile?> GetProfileAsync(Guid principalId, CancellationToken cancellationToken);

    /// <summary>The owner's live sessions, most recently used first; <paramref name="currentSessionId"/> is marked.</summary>
    Task<IReadOnlyList<SessionSummary>> ListLiveSessionsAsync(
        Guid principalId, Guid currentSessionId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Whether the session is live and completed a step-up that has not expired yet.</summary>
    Task<bool> HasFreshStepUpAsync(Guid principalId, Guid sessionId, DateTimeOffset now, CancellationToken cancellationToken);
}
