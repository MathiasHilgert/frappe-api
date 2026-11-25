using Frappe.Http;
using Microsoft.AspNetCore.Http;

namespace Frappe.Identity.Http.Sessions;

/// <summary>
/// The session cookie as the browser credential the host's console origin check asks about (SEC-03):
/// <see cref="IdentitySessionCookie.IsCarriedBy"/>, registered by <see cref="IdentityHttp"/> so the host names nothing of Identity.
/// </summary>
internal sealed class SessionCookieCredential : IBrowserCredential
{
    /// <inheritdoc />
    public bool IsCarriedBy(HttpContext httpContext) => IdentitySessionCookie.IsCarriedBy(httpContext);
}
