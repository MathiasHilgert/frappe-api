using Microsoft.AspNetCore.Http;

namespace Frappe.Identity.Http.Sessions;

/// <summary>
/// The cookie that carries a session for the web console (S8, SEC-02): <c>__Host-frappe-session</c>, <c>HttpOnly</c>, <c>Secure</c>,
/// <c>SameSite=Lax</c>, for the whole host and with no <c>Domain</c> (the <c>__Host-</c> prefix makes a browser refuse it otherwise, so a
/// sibling subdomain cannot plant one). Other clients send the same token as <c>Authorization: Bearer</c>, which wins. The token appears
/// nowhere else than here and in the log-in answer (SEC-07).
/// </summary>
public static class IdentitySessionCookie
{
    /// <summary>The cookie's name.</summary>
    public const string Name = "__Host-frappe-session";

    /// <summary>
    /// Whether the request carries the session cookie as the credential it is judged by, which is when it carries the cookie and no bearer
    /// token (a bearer token wins over the cookie, and a page cannot add that header across sites). It is what the host's console origin check
    /// asks (SEC-03); it reads only the request's headers.
    /// </summary>
    public static bool IsCarriedBy(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var credential = SessionCredential.Of(httpContext.Request);
        return credential.Kind is SessionCredentialKind.Cookie or SessionCredentialKind.AmbiguousCookie;
    }

    /// <summary>Sets the cookie to <paramref name="token"/> until <paramref name="expires"/>, the session's absolute expiry.</summary>
    public static void Append(HttpResponse response, string token, DateTimeOffset expires)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentException.ThrowIfNullOrEmpty(token);
        response.Cookies.Append(Name, token, Options(expires));
    }

    /// <summary>Tells the browser to forget the cookie.</summary>
    public static void Clear(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        response.Cookies.Delete(Name, Options(null));
    }

    private static CookieOptions Options(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Expires = expires,
        IsEssential = true,
    };
}
