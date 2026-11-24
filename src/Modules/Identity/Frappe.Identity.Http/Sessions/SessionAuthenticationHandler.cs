using System.Text.Encodings.Web;
using Frappe.Http;
using Frappe.Identity.Application.Credentials;
using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Application.Sessions.AuthenticateSession;
using Frappe.Identity.Application.Sessions.TouchSession;
using Frappe.Identity.Domain;
using Frappe.Results;
using Frappe.UseCases;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodaTime;

namespace Frappe.Identity.Http.Sessions;

/// <summary>
/// Proves a request's session (S8, SEC-02): the token of an <c>Authorization: Bearer</c> header, which wins, or else of the session cookie.
/// The token is hashed here and the hash handed to <see cref="AuthenticateSession"/>, which re-reads the session and its principal on every
/// request; a token that is not of the issued format never reaches the database. Nothing is cached (SEC-17). A request that carries two
/// session cookies, a bearer token that fails beside a cookie that would pass, or a token nobody holds is refused alike, and every refusal
/// is the same 401 <see cref="IdentityErrorCodes.SessionInvalid"/>. When the session was last touched a minute ago or more it is touched
/// (<see cref="TouchSession"/>); losing the race to another request of the same session is logged and counted, never a failure.
/// </summary>
internal sealed partial class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var credential = SessionCredential.Of(Request);
        if (credential.Kind == SessionCredentialKind.None)
        {
            return AuthenticateResult.NoResult();
        }

        var services = Context.RequestServices;
        var hash = credential.Kind is SessionCredentialKind.Bearer or SessionCredentialKind.Cookie
            ? services.GetRequiredService<ISessionTokens>().HashOf(credential.Token)
            : null;
        if (hash is null)
        {
            return AuthenticateResult.Fail("The presented session credential is not valid.");
        }

        var query = services.GetRequiredService<IQueryHandler<AuthenticateSession, SessionPrincipal>>();
        var authenticated = await query.HandleAsync(new AuthenticateSession(hash), Context.RequestAborted);
        if (!authenticated.IsSuccess)
        {
            return AuthenticateResult.Fail("The session is not valid.");
        }

        var session = authenticated.Value;
        var now = Instant.FromDateTimeOffset(services.GetRequiredService<TimeProvider>().GetUtcNow());
        if (session.IsDueForTouch(now))
        {
            await TouchAsync(services, session.SessionId);
        }

        return AuthenticateResult.Success(new AuthenticationTicket(SessionClaims.PrincipalOf(session, now), Scheme.Name));
    }

    /// <summary>The same 401 for every refusal, with <c>WWW-Authenticate</c>, in the caller's language.</summary>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Result<Unit> invalid = new UnauthenticatedError(IdentityErrorCodes.SessionInvalid);
        return invalid.ToTypedHttpResult(_ => TypedResults.NoContent()).ExecuteAsync(Context);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "A touch of a session lost the race to another request of the same session.")]
    private static partial void LogTouchLost(ILogger logger);

    private async Task TouchAsync(IServiceProvider services, Guid sessionId)
    {
        var touch = services.GetRequiredService<ICommandHandler<TouchSession, Unit>>();
        var touched = await touch.HandleAsync(new TouchSession(sessionId), Context.RequestAborted);
        if (!touched.IsSuccess)
        {
            services.GetRequiredService<IdentityMetrics>().SessionTouchLost();
            LogTouchLost(Logger);
        }
    }
}
