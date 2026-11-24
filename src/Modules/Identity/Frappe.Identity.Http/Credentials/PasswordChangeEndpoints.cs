using Frappe.Http;
using Frappe.Identity.Application.Credentials;
using Frappe.Identity.Application.Credentials.ChangePassword;
using Frappe.Identity.Domain.Credentials;
using Frappe.Identity.Http.Sessions;
using Frappe.Results;
using Frappe.UseCases;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Frappe.Identity.Http.Credentials;

/// <summary>
/// Changing the password of whoever is signed in (S12): <c>POST /me/password-changes</c>. It belongs to the core, so any kind of principal that
/// has a password uses it; the session is the caller's (SEC-01), never named in the URL, and the answer is never cached.
/// </summary>
internal static class PasswordChangeEndpoints
{
    extension(IEndpointRouteBuilder routes)
    {
        public void MapPasswordChangeEndpoints() =>
            routes.MapPost("/me/password-changes", ChangeAsync)
                .WithName("ChangePassword").WithTags(IdentityHttp.Tag).NoStore().WithoutLocation().RequireSession()
                .WithSummary("Change the password")
                .WithDescription(
                    "Replaces the password of the caller. The current password is asked again: 422 identity.password.incorrect when it is not "
                    + $"the one the caller has (never faster than {PasswordCheck.RefusalFloor.TotalMilliseconds:0} ms), and "
                    + $"{PasswordRecheck.Flow.Limit} tries in "
                    + $"{PasswordRecheck.Flow.Window.TotalMinutes:0} minutes are allowed, whatever their outcome, then 429 http.rate_limited "
                    + "with Retry-After (503 identity.attempts.unavailable while tries cannot be counted). The new one has "
                    + $"{Password.MinLength} to {Password.MaxLength} characters and is not in a public breach list (422 "
                    + "identity.password.too_short, too_long or breached). Every other session of the caller is revoked at once; the one the "
                    + "request used keeps working with the same token. It answers 204. 401 identity.session.invalid without a valid session.")
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
                .ProducesProblem(StatusCodes.Status429TooManyRequests)
                .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<Results<NoContent, ErrorsProblemHttpResult>> ChangeAsync(
        [FromBody] ChangePasswordRequest? body, HttpContext httpContext, ICommandHandler<ChangePassword, Unit> handler,
        CancellationToken cancellationToken)
    {
        var principal = SessionPolicy.RequiredPrincipalIdOf(httpContext.User);
        var session = SessionClaims.RequiredSessionIdOf(httpContext.User);
        var command = new ChangePassword(principal, session, body?.CurrentPassword, body?.NewPassword);
        return (await handler.HandleAsync(command, cancellationToken)).ToTypedHttpResult(_ => TypedResults.NoContent());
    }
}
