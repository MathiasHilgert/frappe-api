using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Security;

namespace Frappe.Identity.Application.Sessions;

/// <summary>Resolves an opaque session token to its live session, checking expiry against the clock on every call.</summary>
public sealed class SessionAuthenticator(IPrincipalRepository principals, ISecretHasher hasher, TimeProvider clock)
{
    /// <summary>
    /// Use moves the idle expiry, but writing on every request would turn each read into a write. A session is
    /// touched at most once per interval, so the idle window can come up to this much short of 12 hours.
    /// </summary>
    public static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(5);

    public async Task<AuthenticatedSession?> AuthenticateAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var tokenHash = hasher.Hash(token);
        var principal = await principals.GetBySessionTokenHashAsync(tokenHash, cancellationToken);
        var session = principal?.Sessions.FirstOrDefault(s => string.Equals(s.TokenHash, tokenHash, StringComparison.Ordinal));
        if (principal is null || session is null || !session.IsLive(clock))
            return null;

        if (clock.GetUtcNow() - session.LastSeenAt >= TouchInterval
            && principal.TouchSession(session.Id, clock).IsSuccess)
        {
            await principals.SaveChangesAsync(cancellationToken);
        }

        return new AuthenticatedSession(principal.Id, session.Id, principal.Type);
    }
}
