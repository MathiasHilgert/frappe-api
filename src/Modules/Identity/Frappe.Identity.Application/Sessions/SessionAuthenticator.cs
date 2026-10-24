using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Security;

namespace Frappe.Identity.Application.Sessions;

/// <summary>
/// Resolves an opaque session token to its live session, checking expiry against the clock on every call and
/// rejecting sessions of suspended owners. Every request reads one lean credential; a session due a touch costs one
/// more single-row <c>UPDATE ... WHERE last_seen_at &lt; x</c>, never the aggregate. The credential is not cached:
/// a revocation must take effect on the next request on every instance, and the read is one indexed lookup.
/// </summary>
public sealed class SessionAuthenticator(
    IPrincipalRepository principals, ISessionTouches touches, ISecretHasher hasher, TimeProvider clock)
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
        var credential = await principals.FindSessionCredentialAsync(tokenHash, cancellationToken);
        // A suspended owner is shut out at once, even through sessions opened before the suspension.
        if (credential is null || !credential.Admits(clock))
            return null;

        if (clock.GetUtcNow() - credential.LastSeenAt >= TouchInterval
            && !await TouchAsync(credential, tokenHash, cancellationToken))
        {
            return null;
        }

        return new AuthenticatedSession(credential.PrincipalId, credential.SessionId, credential.PrincipalType);
    }

    /// <summary>
    /// Best effort: when the update changes nothing (another request touched first, or a revocation deleted the
    /// session), the session is admitted only if a fresh read still admits it, so a racing revocation wins.
    /// </summary>
    private async Task<bool> TouchAsync(SessionCredential credential, string tokenHash, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        if (await touches.TouchAsync(credential.SessionId, now, now - TouchInterval, cancellationToken))
            return true;

        var fresh = await principals.FindSessionCredentialAsync(tokenHash, cancellationToken);
        return fresh is not null && fresh.Admits(clock);
    }
}
