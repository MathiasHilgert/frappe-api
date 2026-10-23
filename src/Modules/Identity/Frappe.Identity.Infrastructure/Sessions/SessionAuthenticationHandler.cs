using System.Security.Claims;
using System.Text.Encodings.Web;
using Frappe.Identity.Application.Sessions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Frappe.Identity.Infrastructure.Sessions;

/// <summary>
/// Reads the opaque token from <c>Authorization: Bearer</c> or, failing that, from the session cookie. A cookie is
/// sent by the browser on its own, so unsafe methods carried by it also need <see cref="CsrfHeader"/>: a custom header
/// a cross-site form cannot set and a cross-site script cannot send without a CORS preflight we never allow.
/// </summary>
internal sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    SessionAuthenticator authenticator,
    IOptions<SessionCookieOptions> cookie)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "OwnerSession";
    public const string CsrfHeader = "X-Requested-With";
    private const string BearerPrefix = "Bearer ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? token;
        string authorization = Request.Headers.Authorization.ToString();
        if (authorization.Length > 0)
        {
            if (!authorization.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
                return AuthenticateResult.NoResult();

            token = authorization[BearerPrefix.Length..].Trim();
        }
        else if (Request.Cookies.TryGetValue(cookie.Value.Name, out token))
        {
            // ASP.NET keeps only one value per name; a second copy may be a planted cookie shadowing the real one.
            if (CountCookies(cookie.Value.Name) > 1)
                return AuthenticateResult.Fail("The session cookie was sent more than once.");

            if (!IsSafe(Request.Method) && !Request.Headers.ContainsKey(CsrfHeader))
                return AuthenticateResult.Fail($"Cookie sessions need the {CsrfHeader} header on unsafe methods.");
        }
        else
        {
            return AuthenticateResult.NoResult();
        }

        var session = await authenticator.AuthenticateAsync(token, Context.RequestAborted);
        if (session is null)
            return AuthenticateResult.Fail("Unknown, revoked or expired session.");

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, session.PrincipalId.ToString()),
                new Claim(SessionClaimTypes.SessionId, session.SessionId.ToString()),
                new Claim(SessionClaimTypes.PrincipalType, session.PrincipalType.ToString().ToLowerInvariant()),
            ],
            SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    private int CountCookies(string name)
    {
        var count = 0;
        foreach (var header in Request.Headers.Cookie)
        {
            foreach (var pair in (header ?? string.Empty).Split(';'))
            {
                var trimmed = pair.AsSpan().TrimStart();
                if (trimmed.StartsWith(name, StringComparison.Ordinal) && trimmed[name.Length..].TrimStart().StartsWith("="))
                    count++;
            }
        }

        return count;
    }

    private static bool IsSafe(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method);
}
