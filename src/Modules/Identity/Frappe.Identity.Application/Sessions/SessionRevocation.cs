using Frappe.Identity.Application.Principals;
using Frappe.Identity.Domain;
using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Application.Sessions;

/// <summary>
/// Revoking a session is idempotent, so losing a race (a concurrent touch or step-up of the same principal) is
/// retried once on fresh state instead of failing the caller.
/// </summary>
internal static class SessionRevocation
{
    private const int Attempts = 2;

    /// <summary>Success, <see cref="IdentityErrors.SessionNotFound"/>, or a conflict that survived the retry.</summary>
    public static async Task<Result> RevokeAsync(
        IPrincipalRepository principals, Guid principalId, Guid sessionId, CancellationToken cancellationToken)
    {
        var outcome = Result.Failure(IdentityErrors.SessionNotFound);
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var principal = await principals.GetByIdAsync(principalId, cancellationToken);
            if (principal is null)
                return Result.Failure(IdentityErrors.SessionNotFound);

            var revoked = principal.RevokeSession(sessionId);
            if (revoked.IsFailure)
                return revoked;

            outcome = await principals.SaveAsync(cancellationToken);
            if (outcome.IsSuccess || !outcome.Errors[0].Is(IdentityErrors.ConcurrentChange))
                return outcome;
        }
        return outcome;
    }
}
