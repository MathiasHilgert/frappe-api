using Frappe.Access.Contracts;
using Frappe.UseCases;

namespace Frappe.Identity.Application.Sessions.AuthenticateSession;

/// <summary>
/// Finds who a session token proves (S8): an internal query the authentication of the Http layer asks on every request that carries one,
/// never published. It fails <c>identity.session.invalid</c>, one answer whatever the reason (unknown, expired, revoked, ended, stale stamp,
/// inactive principal), so a caller learns nothing about which it was.
/// </summary>
/// <param name="TokenHash">The hash of the presented token (<see cref="ISessionTokens.HashOf"/>), never the token.</param>
public sealed record AuthenticateSession(byte[] TokenHash)
    : IQuery<SessionPrincipal>, IPublic // proof: trusted entry: the session authentication handler runs it
{
    public override string ToString() => "AuthenticateSession { TokenHash = *** }";
}
