using Frappe.Identity.Application.Owners;
using Frappe.Identity.Infrastructure.Sessions;
using Frappe.Platform.Web.Problems;
using Frappe.SharedKernel.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Wolverine;

namespace Frappe.Identity.Infrastructure.Endpoints;

internal static class OwnerVerificationEndpoints
{
    public static RouteGroupBuilder MapOwnerVerificationEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/owners/{id:guid}/verification", VerifyAsync)
            .AllowAnonymous()
            .WithName("VerifyOwnerEmail")
            .WithSummary("Verifies a pending owner's email with the emailed code and signs them in.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        return group;
    }

    private static async Task<Results<Created<VerifiedOwnerResponse>, LocalizedProblemResult>> VerifyAsync(
        Guid id, OwnerVerificationRequest request, IMessageBus bus, ISessionCookie cookie, CancellationToken cancellationToken)
    {
        var verified = await bus.InvokeAsync<Result<OpenedSession>>(
            new VerifyOwnerEmail(id, request.Code ?? string.Empty), cancellationToken);
        if (!verified.TryGetValue(out var session))
            return verified.ToProblem();

        cookie.Issue(session.Token, session.ExpiresAt);
        return TypedResults.Created(
            $"/v1/identity/sessions/{session.SessionId}",
            new VerifiedOwnerResponse(session.SessionId, session.Token, session.ExpiresAt));
    }
}
