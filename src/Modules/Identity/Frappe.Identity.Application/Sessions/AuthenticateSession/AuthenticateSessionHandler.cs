using Frappe.Identity.Domain;
using Frappe.Results;
using Frappe.UseCases;
using NodaTime;

namespace Frappe.Identity.Application.Sessions.AuthenticateSession;

/// <summary>
/// Handles <see cref="AuthenticateSession"/>: reads the session and its principal together and lets in only what
/// <see cref="Frappe.Identity.Domain.Sessions.SessionAccess.IsActiveAt"/> accepts. It reads and never writes (CQRS-07): touching the session is
/// a command its caller dispatches.
/// </summary>
internal sealed class AuthenticateSessionHandler(ISessionReads reads, TimeProvider time) : IQueryHandler<AuthenticateSession, SessionPrincipal>
{
    public async Task<Result<SessionPrincipal>> HandleAsync(AuthenticateSession query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var access = await reads.FindAccessAsync(query.TokenHash, cancellationToken);
        if (access is null || !access.IsActiveAt(Instant.FromDateTimeOffset(time.GetUtcNow())))
        {
            return new UnauthenticatedError(IdentityErrorCodes.SessionInvalid);
        }

        return SessionPrincipal.Of(access);
    }
}
