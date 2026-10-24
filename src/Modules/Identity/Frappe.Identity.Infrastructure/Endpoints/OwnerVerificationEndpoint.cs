using Frappe.Identity.Application.Owners;
using Frappe.Identity.Infrastructure.Sessions;
using Frappe.Platform.Web.Problems;
using Frappe.SharedKernel.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Wolverine;

namespace Frappe.Identity.Infrastructure.Endpoints;

internal static class OwnerVerificationEndpoint
{
    /// <summary>Only the shape is checked here; whether the code is right is the domain's call.</summary>
    public sealed record Request(string Code);

    public sealed record Response(Guid SessionId, string Token, DateTimeOffset ExpiresAt);

    public static RouteGroupBuilder MapOwnerVerification(this RouteGroupBuilder group)
    {
        group.MapPost("/owners/{id:guid}/verification", HandleAsync)
            .AllowAnonymous()
            .WithName("VerifyOwnerEmail")
            .WithSummary("Verifies a pending owner's email with the emailed code and signs them in.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        return group;
    }

    private static async Task<IResult> HandleAsync(
        Guid id, Request request, IMessageBus bus, ISessionCookie cookie, CancellationToken cancellationToken)
    {
        var verified = await bus.InvokeAsync<Result<OpenedSession>>(
            new VerifyOwnerEmail(id, request.Code ?? string.Empty), cancellationToken);
        if (verified.IsSuccess)
        {
            var session = verified.Value;
            cookie.Issue(session.Token, session.ExpiresAt);
            return TypedResults.Created(
                $"/v1/identity/sessions/{session.SessionId}",
                new Response(session.SessionId, session.Token, session.ExpiresAt));
        }

        // Each error's Kind picks the status: 404 unknown owner, 409 already verified, 429 slow down, 422 otherwise.
        return verified.ToProblem();
    }
}
