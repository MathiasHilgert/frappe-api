using Frappe.Identity.Application.Principals;
using Frappe.Identity.Domain;
using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Application.Sessions;

/// <summary>Lets a signed-in owner see and end their own sessions; another principal's session is simply not found.</summary>
public sealed class OwnerSessions(IPrincipalRepository repository, TimeProvider clock)
{
    public async Task<IReadOnlyList<SessionSummary>> ListAsync(AuthenticatedSession caller, CancellationToken cancellationToken)
    {
        var principal = await repository.GetByIdAsync(caller.PrincipalId, cancellationToken);
        if (principal is null)
        {
            return [];
        }

        return principal.Sessions
            .Where(s => s.IsLive(clock))
            .OrderByDescending(s => s.LastSeenAt)
            .Select(s => new SessionSummary(s.Id, s.CreatedAt, s.LastSeenAt, s.Id == caller.SessionId))
            .ToList();
    }

    public async Task<Result> RevokeAsync(AuthenticatedSession caller, Guid sessionId, CancellationToken cancellationToken)
    {
        var principal = await repository.GetByIdAsync(caller.PrincipalId, cancellationToken);
        if (principal is null)
        {
            return Result.Failure(IdentityErrors.SessionNotFound);
        }

        var revoked = principal.RevokeSession(sessionId);
        return revoked.IsFailure ? revoked : await repository.SaveChangesAsync(cancellationToken);
    }
}
