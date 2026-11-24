using Frappe.Access.Contracts;
using Frappe.Http;
using Microsoft.AspNetCore.Http;

namespace Frappe.Identity.Http.Sessions;

/// <summary>
/// The caller of a web request: who the session scheme authenticated, read lazily from <c>HttpContext.User</c> the first time a rule asks,
/// because the scheme runs after the container builds the scope. Only an identity of the session scheme counts (a claim alone proves
/// nothing); without one, or without an <c>HttpContext</c>, the caller is <see cref="Caller.Anonymous"/>, and anonymous is never kept: the
/// next read looks again, so a caller authenticated later in the same request is seen as it is.
/// </summary>
internal sealed class SessionCaller(IHttpContextAccessor accessor) : ICaller
{
    private Caller? _authenticated;

    public Guid? PrincipalId => Current.PrincipalId;

    public string? Kind => Current.Kind;

    public Guid? SessionId => Current.SessionId;

    public DateTimeOffset? StepUpValidUntil => Current.StepUpValidUntil;

    public bool IsSystem => false;

    private Caller Current => _authenticated ?? Read();

    private Caller Read()
    {
        if (accessor.HttpContext?.User is not { } user
            || !user.Identities.Any(identity => identity.IsAuthenticated && identity.AuthenticationType == SessionAuthentication.Scheme)
            || SessionPolicy.PrincipalIdOf(user) is not { } principal)
        {
            return Caller.Anonymous;
        }

        _authenticated = new Caller(
            principal,
            user.FindFirst(SessionClaims.KindClaim)?.Value,
            SessionClaims.SessionIdOf(user),
            SessionPolicy.StepUpValidUntilOf(user));
        return _authenticated;
    }
}
