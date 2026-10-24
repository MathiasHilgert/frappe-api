using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Frappe.Identity.Infrastructure.Sessions;

internal sealed class HttpSessionCookie(IHttpContextAccessor accessor, IOptions<SessionCookieOptions> options) : ISessionCookie
{
    private HttpResponse Response =>
        accessor.HttpContext?.Response ?? throw new InvalidOperationException("The session cookie needs an HTTP request.");

    public void Issue(string token, DateTimeOffset expiresAt) =>
        Response.Cookies.Append(options.Value.Name, token, CookieOptions(expiresAt));

    public void Clear() => Response.Cookies.Delete(options.Value.Name, CookieOptions(expiresAt: null));

    private CookieOptions CookieOptions(DateTimeOffset? expiresAt) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = options.Value.Path,
        Domain = options.Value.Domain,
        Expires = expiresAt,
        IsEssential = true,
    };
}
