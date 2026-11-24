using System.Security.Claims;
using Frappe.Http;
using Frappe.Identity.Application.Sessions.AuthenticateSession;
using NodaTime;

namespace Frappe.Identity.Http.Sessions;

/// <summary>
/// The claims an authenticated session gives a request (S8): the principal (<see cref="SessionPolicy.PrincipalClaim"/>, <c>sub</c>), the
/// session (<c>sid</c>), the principal's kind (<c>kind</c>, such as <c>owner</c>), how it proved itself (<c>amr</c>, one claim per method) and,
/// only while it still counts, when its step-up runs out (<see cref="SessionPolicy.StepUpUntilClaim"/>).
/// </summary>
public static class SessionClaims
{
    /// <summary>The claim with the id of the session the request authenticated with.</summary>
    public const string SessionClaim = "sid";

    /// <summary>The claim with the principal's kind.</summary>
    public const string KindClaim = "kind";

    /// <summary>The claim with a method the session was proved by (one per method).</summary>
    public const string MethodClaim = "amr";

    /// <summary>The <see cref="KindClaim"/> of an owner.</summary>
    public const string OwnerKind = SessionPrincipal.OwnerKind;

    /// <summary>The id of the session an authenticated request proved itself with; nothing when it did not.</summary>
    public static Guid? SessionIdOf(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (user.Identity is not { IsAuthenticated: true })
        {
            return null;
        }

        return Guid.TryParse(user.FindFirstValue(SessionClaim), out var session) ? session : null;
    }

    /// <summary>The session of a route guarded by <c>RequireSession</c>, which authorization already refused a request without.</summary>
    public static Guid RequiredSessionIdOf(ClaimsPrincipal user) =>
        SessionIdOf(user) ?? throw new InvalidOperationException("A route that requires a session ran without one.");

    internal static ClaimsPrincipal PrincipalOf(SessionPrincipal session, Instant now)
    {
        List<Claim> claims =
        [
            new(SessionPolicy.PrincipalClaim, session.PrincipalId.ToString()),
            new(SessionClaim, session.SessionId.ToString()),
            new(KindClaim, session.Kind),
            .. session.Methods.Select(method => new Claim(MethodClaim, method)),
        ];
        if (session.StepUpUntil is { } until && until > now)
        {
            claims.Add(SessionPolicy.StepUpClaim(until.ToDateTimeOffset()));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, SessionAuthentication.Scheme, SessionPolicy.PrincipalClaim, roleType: null));
    }
}
