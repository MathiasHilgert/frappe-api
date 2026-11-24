using Frappe.Access.Contracts;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Sessions;
using Frappe.Results;
using Frappe.UseCases;
using NodaTime;

namespace Frappe.Identity.Application.Sessions.RevokeMySession;

/// <summary>Handles <see cref="RevokeMySession"/>.</summary>
internal sealed class RevokeMySessionHandler(ISessions sessions, IAuthorizer access, TimeProvider time) : ICommandHandler<RevokeMySession, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RevokeMySession command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var now = Instant.FromDateTimeOffset(time.GetUtcNow());
        var session = await sessions.FindAsync(command.SessionId, cancellationToken);
        if (session is null)
        {
            return new NotFoundError(IdentityErrorCodes.SessionNotFound);
        }

        if (await access.RefuseAsync(SessionActions.Revoke, session, cancellationToken) is { } refusal)
        {
            return refusal;
        }

        if (!session.IsLiveAt(now))
        {
            return new NotFoundError(IdentityErrorCodes.SessionNotFound);
        }

        session.Revoke(RevocationReason.RevokedByHolder, now);
        return Unit.Value;
    }
}
