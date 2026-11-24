using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;

namespace Frappe.Identity.Http.Sessions;

/// <summary>
/// The session credential a request presents (SEC-02): a bearer token wins over the cookie, and a cookie that appears more than once is never
/// taken for one (the framework's own cookie collection would silently keep the first of them, so the raw header lines are counted).
/// </summary>
/// <param name="Kind">What was presented.</param>
/// <param name="Token">The presented token text, when there is exactly one to judge; it is never printed (SEC-07).</param>
internal readonly record struct SessionCredential(SessionCredentialKind Kind, string? Token)
{
    private const string BearerScheme = "Bearer";

    /// <summary>Masked: the token is a secret (SEC-07).</summary>
    public override string ToString() => $"SessionCredential {{ Kind = {Kind}, Token = *** }}";

    public static SessionCredential Of(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var authorization = request.Headers.Authorization;
        if (authorization.Count > 1)
        {
            return new SessionCredential(SessionCredentialKind.AmbiguousBearer, null);
        }

        // A header of another scheme is not ours: the cookie, if any, is what the request carries.
        if (authorization.Count == 1 && AuthenticationHeaderValue.TryParse(authorization[0], out var header)
            && string.Equals(header.Scheme, BearerScheme, StringComparison.OrdinalIgnoreCase))
        {
            return new SessionCredential(SessionCredentialKind.Bearer, header.Parameter);
        }

        string? cookie = null;
        var count = 0;
        foreach (var line in request.Headers.Cookie)
        {
            foreach (var pair in (line ?? string.Empty).Split(';'))
            {
                var separator = pair.IndexOf('=', StringComparison.Ordinal);
                if (separator > 0 && pair.AsSpan(0, separator).Trim().SequenceEqual(IdentitySessionCookie.Name))
                {
                    cookie = pair[(separator + 1)..].Trim();
                    count++;
                }
            }
        }

        return count switch
        {
            0 => new SessionCredential(SessionCredentialKind.None, null),
            1 => new SessionCredential(SessionCredentialKind.Cookie, cookie),
            _ => new SessionCredential(SessionCredentialKind.AmbiguousCookie, null),
        };
    }
}
