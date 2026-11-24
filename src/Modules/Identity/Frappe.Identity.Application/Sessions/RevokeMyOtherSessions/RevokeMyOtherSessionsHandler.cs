using Frappe.Identity.Domain.Sessions;
using Frappe.Results;
using Frappe.UseCases;
using NodaTime;

namespace Frappe.Identity.Application.Sessions.RevokeMyOtherSessions;

/// <summary>Handles <see cref="RevokeMyOtherSessions"/>: every revocation goes through its aggregate, so each raises its own event.</summary>
internal sealed class RevokeMyOtherSessionsHandler(ISessions sessions, TimeProvider time) : ICommandHandler<RevokeMyOtherSessions, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RevokeMyOtherSessions command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var now = Instant.FromDateTimeOffset(time.GetUtcNow());
        foreach (var session in await sessions.ListLiveOfAsync(command.PrincipalId, now, cancellationToken))
        {
            if (session.Id != command.CurrentSessionId)
            {
                session.Revoke(RevocationReason.OthersRevoked, now);
            }
        }

        return Unit.Value;
    }
}
