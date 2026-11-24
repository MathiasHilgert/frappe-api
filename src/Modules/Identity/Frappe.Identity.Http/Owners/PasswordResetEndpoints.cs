using Frappe.Http;
using Frappe.Identity.Application.Codes;
using Frappe.Identity.Application.Credentials;
using Frappe.Identity.Application.Owners.RequestPasswordReset;
using Frappe.Identity.Application.Owners.ResetPassword;
using Frappe.Identity.Domain.Credentials;
using Frappe.Results;
using Frappe.UseCases;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Frappe.Identity.Http.Owners;

/// <summary>
/// Forgetting a password (S11): <c>POST /password-resets</c> asks for a code and <c>POST /password-resets/{resetId}/confirmation</c> spends it on
/// a new password. Both are anonymous and never cached; neither has anything to follow, so none answers a <c>Location</c>. The id is read as
/// text and judged by the use case: a malformed id is an unknown one.
/// </summary>
internal static class PasswordResetEndpoints
{
    extension(IEndpointRouteBuilder routes)
    {
        public void MapPasswordResetEndpoints()
        {
            routes.MapPost("/password-resets", RequestAsync)
                .WithName("RequestPasswordReset").WithTags(IdentityHttp.Tag).NoStore().WithoutLocation()
                .RequireRateLimiting(IdentityHttp.PasswordResetRateLimitPolicy)
                .WithSummary("Ask for a password reset code")
                .WithDescription(
                    $"Mails a {CodeIssuer.Digits}-digit code to the verified owner who has this address. It answers 202 always with an id to "
                    + "confirm the reset with, for an address that is unknown, malformed, not verified or over its budget of "
                    + $"{CodeLimits.Send.Limit} codes an hour as for one that was sent a code (the id of an address that was sent none is "
                    + $"made up and confirms nothing), and never sooner than {PasswordResetLimits.Floor.TotalMilliseconds:0} ms, "
                    + "so neither the answer nor its time says which. "
                    + "The language of the mail is the owner's own.");

            routes.MapPost("/password-resets/{resetId}/confirmation", ConfirmAsync)
                .WithName("ConfirmPasswordReset").WithTags(IdentityHttp.Tag).NoStore().WithoutLocation()
                .RequireRateLimiting(IdentityHttp.PasswordResetRateLimitPolicy)
                .WithSummary("Reset a password with its code")
                .WithDescription(
                    "Sets a new password with the code that was mailed: every session of the owner is revoked, so the owner logs in again. The "
                    + $"password has {Password.MinLength} to {Password.MaxLength} characters and is not in a public breach list (422 "
                    + "identity.password.too_short, too_long or breached). 422 identity.code.invalid for a code that is wrong, expired, used "
                    + "or replaced and for an id that is unknown, made up or malformed (one answer for all of them): a code takes "
                    + $"{CodeLimits.Guess.Limit} guesses, an owner {CodeLimits.GuessPerPrincipal.Limit} a day from one network "
                    + $"and {CodeLimits.GuessPerPrincipalAnywhere.Limit} a day from all of them across all their codes. "
                    + "503 identity.attempts.unavailable when guesses cannot be counted, for every id alike.")
                .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
                .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<Results<Accepted<PasswordResetResponse>, ErrorsProblemHttpResult>> RequestAsync(
        [FromBody] RequestPasswordResetRequest? body, ICommandHandler<RequestPasswordReset, PasswordResetRequested> handler, TimeProvider time,
        CancellationToken cancellationToken)
    {
        // The floor starts before the handler and ends after it, and its unit of work committed with it: only the branch that sends a code writes.
        var started = time.GetTimestamp();
        var result = await handler.HandleAsync(new RequestPasswordReset(body?.Email), cancellationToken);
        await ResponseFloor.HoldAsync(time, started, PasswordResetLimits.Floor, cancellationToken);
        return result.ToTypedHttpResult(requested => TypedResults.Accepted((string?)null, new PasswordResetResponse(requested.Id)));
    }

    private static async Task<Results<NoContent, ErrorsProblemHttpResult>> ConfirmAsync(
        string resetId, [FromBody] ConfirmPasswordResetRequest? body, ICommandHandler<ResetPassword, Unit> handler,
        HttpContext httpContext, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(
            new ResetPassword(resetId, body?.Code, body?.NewPassword, httpContext.Connection.RemoteIpAddress), cancellationToken))
            .ToTypedHttpResult(_ => TypedResults.NoContent());
}
