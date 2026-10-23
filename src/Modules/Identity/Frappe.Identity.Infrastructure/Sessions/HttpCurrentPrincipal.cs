using System.Security.Claims;
using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Domain.Principals;
using Microsoft.AspNetCore.Http;

namespace Frappe.Identity.Infrastructure.Sessions;

/// <summary>Reads the current session back from the claims <see cref="SessionAuthenticationHandler"/> issued.</summary>
internal sealed class HttpCurrentPrincipal(IHttpContextAccessor accessor) : ICurrentPrincipal
{
    public AuthenticatedSession? Session
    {
        get
        {
            var user = accessor.HttpContext?.User;
            if (user?.Identity?.AuthenticationType != SessionAuthenticationHandler.SchemeName
                || !Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var principalId)
                || !Guid.TryParse(user.FindFirstValue(SessionClaimTypes.SessionId), out var sessionId)
                || !Enum.TryParse<PrincipalType>(user.FindFirstValue(SessionClaimTypes.PrincipalType), ignoreCase: true, out var type))
            {
                return null;
            }

            return new AuthenticatedSession(principalId, sessionId, type);
        }
    }
}
