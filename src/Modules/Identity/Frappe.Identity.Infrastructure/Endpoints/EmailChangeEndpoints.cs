using Frappe.Identity.Application.Account;
using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Infrastructure.Sessions;
using Frappe.Platform.Web.Problems;
using Frappe.SharedKernel.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Wolverine;

namespace Frappe.Identity.Infrastructure.Endpoints;

/// <summary>Changing the sign-in email: request after a step-up, then confirm with the code sent to the new address.</summary>
internal static class EmailChangeEndpoints
{
    private const string Path = "/me/email-changes";

    public static RouteGroupBuilder MapEmailChangeEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost(Path, RequestAsync)
            .RequireAuthorization(IdentityPolicies.OwnerSelf)
            .RequireStepUp()
            .WithName("RequestEmailChange")
            .WithSummary("Starts an email change, emailing a code to the new address. Needs a fresh step-up.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        group.MapPost($"{Path}/{{id:guid}}/confirmation", ConfirmAsync)
            .RequireAuthorization(IdentityPolicies.OwnerSelf)
            .WithName("ConfirmEmailChange")
            .WithSummary("Confirms an email change with the code sent to the new address.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        return group;
    }

    private static async Task<Results<Created<EmailChangeResponse>, LocalizedProblemResult>> RequestAsync(
        EmailChangeRequest request, ICurrentPrincipal current, IMessageBus bus, HttpRequest http,
        CancellationToken cancellationToken)
    {
        var result = await bus.InvokeAsync<Result<Guid>>(
            new RequestEmailChange(current.RequiredSession().PrincipalId, request.NewEmail), cancellationToken);

        return result.TryGetValue(out var id)
            ? TypedResults.Created($"{http.PathBase}{http.Path}/{id}", new EmailChangeResponse(id))
            : result.ToProblem();
    }

    private static async Task<Results<NoContent, LocalizedProblemResult>> ConfirmAsync(
        Guid id, EmailChangeConfirmationRequest request, ICurrentPrincipal current, IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var result = await bus.InvokeAsync<Result>(
            new ConfirmEmailChange(current.RequiredSession().PrincipalId, id, request.Code), cancellationToken);

        // Each error's Kind picks the status: 409 email taken, 404 no such change, 429 slow down, 422 otherwise.
        return result.IsSuccess ? TypedResults.NoContent() : result.ToProblem();
    }
}
