using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Domain.Principals;
using Frappe.Identity.Domain.Sessions;
using NodaTime;

namespace Frappe.Identity.Application.Credentials;

/// <summary>
/// The single place where a credential change rotates the principal's credential stamp and settles every live session (S2): the session of the
/// holder who made the change, when there is one, is restamped and so keeps working; every other is revoked for the reason of the change. It
/// goes through the sessions as tracked aggregates, each with its own event and version, in the unit of the command that changed the
/// credential, so a session that is opened by a concurrent log-in under the old stamp is rejected at authentication (the stamp differs) and one
/// that was read here is revoked.
/// </summary>
public sealed class CredentialChange(ISessions sessions, TimeProvider time)
{
    /// <summary>
    /// Rotates the stamp of <paramref name="principal"/>, restamps its session <paramref name="keptSessionId"/> (none when it is
    /// <see langword="null"/> or not one of its live sessions) and revokes the others for <paramref name="reason"/>.
    /// </summary>
    public async Task ApplyAsync(Principal principal, RevocationReason reason, Guid? keptSessionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(principal);
        principal.RotateStamp();
        var now = Instant.FromDateTimeOffset(time.GetUtcNow());
        foreach (var session in await sessions.ListLiveOfAsync(principal.Id, cancellationToken))
        {
            if (session.Id == keptSessionId)
            {
                session.Restamp(principal.CredentialStamp);
            }
            else
            {
                session.Revoke(reason, now);
            }
        }
    }
}
