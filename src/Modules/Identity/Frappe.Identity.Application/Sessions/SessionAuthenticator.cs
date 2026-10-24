using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Security;

namespace Frappe.Identity.Application.Sessions;

/// <summary>
/// Resolves an opaque session token to its live session, checking expiry against the clock on every call and
/// rejecting sessions of suspended owners. Most requests only read a lean credential; the aggregate is loaded and
/// tracked only when the session is due a touch.
/// </summary>
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
    /// Best effort: a touch that loses a race (another request changed the principal meanwhile) is skipped, and the
    /// session is admitted only if a fresh read still admits it, so a revocation racing the touch wins without a 500.
    /// </summary>
    private async Task<bool> TouchAsync(SessionCredential credential, string tokenHash, CancellationToken cancellationToken)
    {
        var principal = await principals.GetByIdAsync(credential.PrincipalId, cancellationToken);
        if (principal is null || principal.TouchSession(credential.SessionId, clock).IsFailure)
            return false;
        if (await principals.SaveUnlessChangedAsync(cancellationToken))
            return true;

        var fresh = await principals.FindSessionCredentialAsync(tokenHash, cancellationToken);
        return fresh is not null && fresh.Admits(clock);
    }
}
